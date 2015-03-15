﻿using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Data;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Runtime.Serialization;
using System.Text;
using System.Windows.Forms;
using System.Xml;
using System.Xml.Serialization;

using Ookii.Dialogs;
using RocksmithToolkitLib.Song2014ToTab;
using X360.STFS;

using RocksmithToolkitLib;
using RocksmithToolkitLib.DLCPackage;
using RocksmithToolkitLib.DLCPackage.Manifest.Tone;
using RocksmithToolkitLib.Extensions;
using RocksmithToolkitLib.Ogg;
using RocksmithToolkitLib.Sng;
using RocksmithToolkitLib.ToolkitTone;
using RocksmithToolkitLib.Xml;
using Control = System.Windows.Forms.Control;

namespace RocksmithToolkitGUI.DLCPackageCreator
{
    public partial class DLCPackageCreator : UserControl
    {
        public static readonly string MESSAGEBOX_CAPTION = "DLC Package Creator";
        private BackgroundWorker bwGenerate = new BackgroundWorker();
        private StringBuilder errorsFound;
        private string dlcSavePath;
        private int userChanges;

        #region Properties

        public GameVersion CurrentGameVersion
        {
            get
            {
                if (RS2014.Checked)
                    return GameVersion.RS2014;
                if (RS2012.Checked)
                    return GameVersion.RS2012;
                if (rbConvert.Checked)
                    return GameVersion.None;

                throw new InvalidDataException("Game version is missing or is not selected.");
            }
            set
            {
                switch (value)
                {
                    case GameVersion.RS2014:
                        RS2014.Checked = true;
                        break;
                    case GameVersion.RS2012:
                        RS2012.Checked = true;
                        break;
                    case GameVersion.None:
                        rbConvert.Checked = true;
                        break;
                }
            }
        }

        private string CurrentOFDAudioFileFilter
        {
            get
            {
                switch (CurrentGameVersion)
                {
                    case GameVersion.None:
                    case GameVersion.RS2014:
                        // TODO: Test WEM generations with non-PC Platforms
                        if (platformMAC.Checked == platformPS3.Checked == platformXBox360.Checked == false)
                            return "All Supported Files|*.wem;*.ogg;*.wav|Wwise 2013 audio files (*.wem)|*.wem|Ogg Vobis audio files (*.ogg)|*.ogg|Wave audio files (*.wav)|*.wav";

                        return "Wwise 2013 audio files (*.wem)|*.wem";
                    default:
                        return "Wwise 2010 audio files (*.ogg)|*.ogg";
                }
            }
        }

        private string CurrentOFDToneImportFilter
        {
            get
            {
                var filter = CurrentOFDPackageFilter + "|";
                filter += CurrentRocksmithTitle + " Song Manifest (*.json)|*.json";

                return filter;
            }
        }

        public string CurrentOFDPackageFilter
        {
            get
            {
                string filter;
                switch (CurrentGameVersion)
                {
                    // case GameVersion.None:
                    case GameVersion.RS2014:
                        filter = CurrentRocksmithTitle + " PC/Mac Package (*.psarc)|*.psarc|";
                        filter += CurrentRocksmithTitle + " XBox360 Package (*.*)|*.*|";
                        filter += CurrentRocksmithTitle + " PS3 Package (*.edat)|*.edat";
                        break;
                    default:
                        filter = CurrentRocksmithTitle + " PC/Mac Package (*.dat)|*.dat|";
                        filter += CurrentRocksmithTitle + " XBox360 Package (*.*)|*.*";
                        break;
                }
                return filter;
            }
        }

        private string CurrentRocksmithTitle
        {
            get
            {
                switch (CurrentGameVersion)
                {
                    case GameVersion.None:
                    case GameVersion.RS2014:
                        return "Rocksmith 2014";
                    default:
                        return "Rocksmith";
                }
            }
        }

        //Song Information
        public string DLCName
        {
            get { return DlcNameTB.Text; }
            set { DlcNameTB.Text = value.GetValidSongName(SongTitle); }
        }

        public string SongTitle
        {
            get { return SongDisplayNameTB.Text; }
            set { SongDisplayNameTB.Text = value; }
        }

        public string SongTitleSort
        {
            get { return SongDisplayNameSortTB.Text; }
            set { SongDisplayNameSortTB.Text = value; }
        }

        public string Album
        {
            get { return AlbumTB.Text; }
            set { AlbumTB.Text = value; }
        }

        public string Artist
        {
            get { return ArtistTB.Text; }
            set { ArtistTB.Text = value; }
        }

        public string ArtistSort
        {
            get { return ArtistSortTB.Text; }
            set { ArtistSortTB.Text = value; }
        }

        public string AlbumYear
        {
            get { return YearTB.Text; }
            set { YearTB.Text = value; }
        }

        public string AppId
        {
            get { return AppIdTB.Text; }
            set { AppIdTB.Text = value; }
        }

        public string AverageTempo
        {
            get { return AverageTempoTB.Text; }
            set { AverageTempoTB.Text = value; }
        }

        public string PackageVersion
        {
            get { return packageVersionTB.Text; }
            set { packageVersionTB.Text = value; }
        }

        //Tones
        private IEnumerable<string> GetToneNames()
        {
            if (CurrentGameVersion != GameVersion.RS2012)
                return tonesLB.Items.OfType<Tone2014>().Select(t => t.Name);
            else
                return tonesLB.Items.OfType<Tone>().Select(t => t.Name);
        }

        //Files
        private string AlbumArtPath // 512 (RS1)
        {
            get { return albumArtPathTB.Text; }
            set { albumArtPathTB.Text = value; }
        }

        private string AudioPath
        {
            get { return audioPathTB.Text; }
            set { audioPathTB.Text = value; }
        }

        public string LyricArtPath
        {
            get;
            set;
        }

        #endregion

        public DLCPackageCreator()
        {
            InitializeComponent();
            audioQualityBox.MouseEnter += audioQualityBox_MouseEnter;
            rbConvert.MouseEnter += rbConvert_MouseEnter;
            songVolumeBox.MouseEnter += songVolumeBox_MouseEnter;
            previewVolumeBox.MouseEnter += songVolumeBox_MouseEnter;
            AddOnChangeHandlerToInputControls();

            try
            {
                PopulateAppIdCombo();
                PopulateTonesLB();
                SetDefaultFromConfig();
                platformPC.Checked = true;
                platformMAC.Checked = platformPS3.Checked = platformXBox360.Checked = false;
                RS2014.Checked = true;
                CurrentGameVersion = GameVersion.RS2014;
            }
            catch { /*For mono compatibility*/ }

            // Generate package worker
            bwGenerate.DoWork += GeneratePackage;
            bwGenerate.ProgressChanged += ProgressChanged;
            bwGenerate.RunWorkerCompleted += ProcessCompleted;
            bwGenerate.WorkerReportsProgress = true;
        }

        private void SetDefaultFromConfig()
        {
            CurrentGameVersion = (GameVersion)Enum.Parse(typeof(GameVersion), ConfigRepository.Instance()["general_defaultgameversion"]);
        }

        private void PopulateTonesLB()
        {
            tonesLB.Items.Clear();
            tonesLB.Items.Add(CreateNewTone());
        }

        public dynamic CreateNewTone(string toneName = "Default")
        {
            var name = GetUniqueToneName(toneName);

            if (CurrentGameVersion != GameVersion.RS2012)
                return new Tone2014() { Name = name, Key = name };
            else
                return new Tone() { Name = name, Key = name };
        }

        private string GetUniqueToneName(string toneName)
        {
            var uniqueName = toneName;
            bool isUnique = false;
            int ind = 1;

            do
            {
                switch (CurrentGameVersion)
                {
                    case GameVersion.RS2012:
                        isUnique = tonesLB.Items.OfType<Tone>().All(n => n.Name != uniqueName);
                        break;
                    case GameVersion.None:
                    case GameVersion.RS2014:
                        isUnique = tonesLB.Items.OfType<Tone2014>().All(n => n.Name != uniqueName);
                        break;
                }

                if (!isUnique)
                {
                    uniqueName = toneName + (++ind);
                }
            } while (!isUnique);

            return uniqueName;
        }

        public void arrangementAddButton_Click(object sender = null, EventArgs e = null)
        {
            Arrangement arrangement = null;
            using (var form = new ArrangementForm(this, CurrentGameVersion))
            {
                if (DialogResult.OK == form.ShowDialog())
                    arrangement = form.Arrangement;
            }
            if (arrangement == null)
                return;
            arrangementLB.Items.Add(arrangement);
        }

        private void arrangementRemoveButton_Click(object sender, EventArgs e)
        {
            if (MessageBox.Show("Are you sure to delete the selected arrangement?", MESSAGEBOX_CAPTION, MessageBoxButtons.YesNo, MessageBoxIcon.Warning) == DialogResult.No)
                return;

            if (arrangementLB.SelectedItem != null)
                arrangementLB.Items.Remove(arrangementLB.SelectedItem);
        }
        //TODO: allow to choose audio for each arrangement separately. #Lessons, #Multitracks
        private void openAudioButton_Click(object sender, EventArgs e)
        {
            using (var ofd = new OpenFileDialog())
            {
                ofd.Filter = CurrentOFDAudioFileFilter;
                if (ofd.ShowDialog() == DialogResult.OK)
                    AudioPath = ofd.FileName;
            }
        }

        public void dlcGenerateButton_Click(object sender = null, EventArgs e = null)
        {
            var packageData = GetPackageData();

            if (packageData == null)
            {
                MessageBox.Show("One or more fields are missing information.", MESSAGEBOX_CAPTION, MessageBoxButtons.OK, MessageBoxIcon.Error);
                return;
            }

            using (var ofd = new SaveFileDialog())
            {
                ofd.FileName = GeneralExtensions.GetShortName("{0}_{1}_v{2}", ArtistSort, SongTitleSort, PackageVersion.Replace(".", "_"), ConfigRepository.Instance().GetBoolean("creator_useacronyms"));
                ofd.Filter = CurrentRocksmithTitle + " DLC (*.*)|*.*";
                if (ofd.ShowDialog() != DialogResult.OK) return;
                dlcSavePath = ofd.FileName;
            }

            //Generate metronome arrangemnts here
            var mArr = new List<Arrangement>();
            foreach (var arr in packageData.Arrangements)
            {
                if (arr.Metronome == Metronome.Generate)
                    mArr.Add(GenMetronomeArr(arr));

                if (userChanges > 0 && arr.ArrangementType != ArrangementType.Vocal)
                    UpdateXml(arr, packageData);
            }

            packageData.Arrangements.AddRange(mArr);

            if (Path.GetFileName(dlcSavePath).Contains(" ") && platformPS3.Checked)
                if (!ConfigRepository.Instance().GetBoolean("creator_ps3pkgnamewarn"))
                {
                    MessageBox.Show(String.Format("PS3 package name can't support space character due to encryption limitation. {0} Spaces will be automatic removed for your PS3 package name.", Environment.NewLine), MESSAGEBOX_CAPTION, MessageBoxButtons.OK, MessageBoxIcon.Warning);
                }
                else
                {
                    ConfigRepository.Instance()["creator_ps3pkgnamewarn"] = true.ToString();
                }

            if (!bwGenerate.IsBusy && packageData != null)
            {
                updateProgress.Visible = true;
                currentOperationLabel.Visible = true;
                dlcGenerateButton.Enabled = false;
                bwGenerate.RunWorkerAsync(packageData);
            }
        }

        private void GeneratePackage(object sender, DoWorkEventArgs e)
        {
            var currentGameVersion = (CurrentGameVersion == GameVersion.RS2012) ? GameVersion.RS2012 : GameVersion.RS2014;
            var packageData = e.Argument as DLCPackageData;
            errorsFound = new StringBuilder();

            var numPlatforms = 0;
            if (platformPC.Checked)
                numPlatforms++;
            if (platformMAC.Checked)
                numPlatforms++;
            if (platformXBox360.Checked)
                numPlatforms++;
            if (platformPS3.Checked)
                numPlatforms++;

            var step = (int)Math.Round(1.0 / numPlatforms * 100, 0);
            int progress = 0;

            if (platformPC.Checked)
                try
                {
                    bwGenerate.ReportProgress(progress, "Generating PC package");
                    RocksmithToolkitLib.DLCPackage.DLCPackageCreator.Generate(dlcSavePath, packageData, new Platform(GamePlatform.Pc, currentGameVersion), pnum: numPlatforms);
                    progress += step; numPlatforms--;
                    bwGenerate.ReportProgress(progress);
                }
                catch (Exception ex)
                {
                    errorsFound.AppendLine(String.Format("Error generate PC package: {0}{1}{0}{2}{0}", Environment.NewLine, ex.Message, ex.StackTrace));
                }

            if (platformMAC.Checked)
                try
                {
                    bwGenerate.ReportProgress(progress, "Generating Mac package");
                    RocksmithToolkitLib.DLCPackage.DLCPackageCreator.Generate(dlcSavePath, packageData, new Platform(GamePlatform.Mac, currentGameVersion), pnum: numPlatforms);
                    progress += step; numPlatforms--;
                    bwGenerate.ReportProgress(progress);
                }
                catch (Exception ex)
                {
                    errorsFound.AppendLine(String.Format("Error generate Mac package: {0}{1}{0}{2}{0}", Environment.NewLine, ex.Message, ex.StackTrace));
                }

            if (platformXBox360.Checked)
                try
                {
                    bwGenerate.ReportProgress(progress, "Generating XBox 360 package");
                    RocksmithToolkitLib.DLCPackage.DLCPackageCreator.Generate(dlcSavePath, packageData, new Platform(GamePlatform.XBox360, currentGameVersion), pnum: numPlatforms);
                    progress += step; numPlatforms--;
                    bwGenerate.ReportProgress(progress);
                }
                catch (Exception ex)
                {
                    errorsFound.AppendLine(String.Format("Error generate XBox 360 package: {0}{1}{0}{2}{0}", Environment.NewLine, ex.Message, ex.StackTrace));
                }

            if (platformPS3.Checked)
                try
                {
                    bwGenerate.ReportProgress(progress, "Generating PS3 package");
                    RocksmithToolkitLib.DLCPackage.DLCPackageCreator.Generate(dlcSavePath, packageData, new Platform(GamePlatform.PS3, currentGameVersion), pnum: numPlatforms);
                    progress += step; numPlatforms--;
                    bwGenerate.ReportProgress(progress);
                }
                catch (Exception ex)
                {
                    errorsFound.AppendLine(String.Format("Error generate PS3 package: {0}{1}. {0}PS3 package require 'JAVA x86' (32 bits) installed on your machine to generate properly.{0}", Environment.NewLine, ex.StackTrace));
                }

            // Cache cleanup so we don't serialize or reuse data that could be changed
            packageData.CleanCache();
            e.Result = (numPlatforms == 1 && errorsFound.Length > 0) ? "error" : "generate";
        }

        private void albumArtButton_Click(object sender, EventArgs e)
        {
            if (String.IsNullOrEmpty(DlcNameTB.Text))
            {
                MessageBox.Show("Fill the 'DLC Name' field first.", MESSAGEBOX_CAPTION, MessageBoxButtons.OK, MessageBoxIcon.Warning);
                DlcNameTB.Focus();
                return;
            }
            using (var ofd = new OpenFileDialog())
            {
                ofd.Filter = "Album Art File (*.bmp,*.dds,*.gif,*.jpg,*.jpeg,*.png)|*.bmp;*.dds;*.gif;*.jpg;*.jpeg;*.png";
                if (ofd.ShowDialog() == DialogResult.OK)
                {
                    if (ofd.FileName.IsValidImage())
                        AlbumArtPath = ofd.FileName;
                    else
                        MessageBox.Show("The selected image is not valid or not supported." + Environment.NewLine +
                                        "MimeType doesn't match with file extension!", MESSAGEBOX_CAPTION, MessageBoxButtons.OK, MessageBoxIcon.Exclamation);
                }
            }
        }

        private void dlcSaveButton_Click(object sender, EventArgs e)
        {
            SaveTemplateFile();
        }

        public void SaveTemplateFile(string defaultSavePath = null)
        {
            var arrangements = arrangementLB.Items.OfType<Arrangement>().ToList();
            var packageData = GetPackageData();
            if (packageData == null)
            {
                MessageBox.Show("One or more fields are missing information.", MESSAGEBOX_CAPTION, MessageBoxButtons.OK, MessageBoxIcon.Error);
                return;
            }

            string fileName = GeneralExtensions.GetShortName("{0}_{1}_{2}", ArtistSort, SongTitleSort, CurrentGameVersion.ToString(), ConfigRepository.Instance().GetBoolean("creator_useacronyms"));

            if (!String.IsNullOrEmpty(defaultSavePath))
            {
                dlcSavePath = Path.Combine(defaultSavePath, fileName + ".dlc.xml");
            }
            else
            {
                using (var ofd = new SaveFileDialog())
                {
                    ofd.SupportMultiDottedExtensions = true;
                    ofd.Filter = CurrentRocksmithTitle + " DLC Template (*.dlc.xml)|*.dlc.xml";
                    ofd.FileName = fileName;
                    if (DialogResult.OK != ofd.ShowDialog()) return;
                    dlcSavePath = ofd.FileName;
                }
            }

            //Make the paths relative
            var BasePath = Path.GetDirectoryName(dlcSavePath);
            if (!string.IsNullOrEmpty(packageData.AlbumArtPath))
                packageData.AlbumArtPath = packageData.AlbumArtPath.RelativeTo(BasePath);

            string audioPath = packageData.OggPath;
            string audioPreviewPath = packageData.OggPreviewPath;
            if (!String.IsNullOrEmpty(audioPath))
                packageData.OggPath = audioPath.RelativeTo(BasePath);
            if (!String.IsNullOrEmpty(audioPreviewPath))
                packageData.OggPreviewPath = audioPreviewPath.RelativeTo(BasePath);

            foreach (var arr in packageData.Arrangements)
            {
                if (String.IsNullOrEmpty(arr.SongXml.File))
                    continue;
                arr.SongXml.File = arr.SongXml.File.RelativeTo(BasePath);
                arr.SongFile.File = "";
                if (!String.IsNullOrEmpty(arr.FontSng))
                    arr.FontSng = arr.FontSng.RelativeTo(BasePath);
            }

            using (var stm = XmlWriter.Create(dlcSavePath, new XmlWriterSettings { CheckCharacters = true, Indent = true }))
            {
                new DataContractSerializer(typeof(DLCPackageData)).WriteObject(stm, packageData);
            }

            //Re-absolutize the paths
            foreach (var arr in packageData.Arrangements)
            {
                if (!String.IsNullOrEmpty(arr.SongXml.File))
                    arr.SongXml.File = arr.SongXml.File.AbsoluteTo(BasePath);
                if (!String.IsNullOrEmpty(arr.SongFile.File))
                    arr.SongFile.File = arr.SongFile.File.AbsoluteTo(BasePath);
                if (!String.IsNullOrEmpty(arr.FontSng))
                    arr.FontSng = arr.FontSng.AbsoluteTo(BasePath);
            }

            if (String.IsNullOrEmpty(defaultSavePath))//if in GUI mode.
                MessageBox.Show(CurrentRocksmithTitle + " DLC Package template was saved.", MESSAGEBOX_CAPTION, MessageBoxButtons.OK, MessageBoxIcon.Information);
        }

        public void dlcLoadButton_Click(object sender = null, EventArgs e = null)
        {
            string dlcLoadPath;
            using (var ofd = new OpenFileDialog())
            {
                ofd.SupportMultiDottedExtensions = true;
                ofd.Filter = CurrentRocksmithTitle + " DLC Template (*.dlc.xml)|*.dlc.xml";
                if (ofd.ShowDialog() != DialogResult.OK) return;
                dlcLoadPath = ofd.FileName;
            }

            loadTemplate(dlcLoadPath);
        }

        public void loadTemplate(string dlcLoadPath)
        {
            DLCPackageData info;
            try
            {
                using (var stm = new XmlTextReader(dlcLoadPath))
                {
                    info = new DataContractSerializer(typeof(DLCPackageData)).ReadObject(stm) as DLCPackageData;
                }
                if (info == null) throw new InvalidDataException("DLC Template Is Null");
            }
            catch (Exception se)
            {
                MessageBox.Show("Can't load DLC Template because it's not compatible with new DLC Template format. \n" + se.Message, MESSAGEBOX_CAPTION, MessageBoxButtons.OK, MessageBoxIcon.Error);
                return;
            }

            // use AppId to determine GameVersion for dlc.xml
            RS2012.Checked = (Convert.ToInt32(info.AppId) < 230000);
            RS2014.Checked = (Convert.ToInt32(info.AppId) > 240000);

            FillPackageCreatorForm(info, dlcLoadPath);

            Application.DoEvents();
            MessageBox.Show(CurrentRocksmithTitle + " DLC Template was loaded.", MESSAGEBOX_CAPTION, MessageBoxButtons.OK, MessageBoxIcon.Information);
            Parent.Focus();
        }

        public void dlcImportButton_Click(object sender = null, EventArgs e = null)
        {
            string sourcePackage;
            string savePath;
            string tmp = Path.GetTempPath();

            // GET PATH
            using (var ofd = new OpenFileDialog())
            {
                ofd.Title = "Select one DLC to import";
                ofd.Filter = CurrentOFDPackageFilter;

                if (ofd.ShowDialog() != DialogResult.OK)
                    return;
                sourcePackage = ofd.FileName;
            }

            if (CurrentGameVersion == GameVersion.RS2014)
                if (!sourcePackage.IsValidPSARC())
                {
                    MessageBox.Show(String.Format("Invalid File Exception:  File '{0}' can not be used by current process.", Path.GetFileName(sourcePackage)), MESSAGEBOX_CAPTION, MessageBoxButtons.OK, MessageBoxIcon.Error);
                    return;
                }

            using (var fbd = new VistaFolderBrowserDialog())
            {
                fbd.Description = "Select folder to save project artifacts";
                fbd.UseDescriptionForTitle = true;
                fbd.SelectedPath = Path.GetDirectoryName(sourcePackage);
                if (fbd.ShowDialog() != DialogResult.OK)
                    return;
                savePath = fbd.SelectedPath;
            }

            // UNPACK
            var packagePlatform = sourcePackage.GetPlatform();
            var unpackedDir = Packer.Unpack(sourcePackage, tmp, true, true, false);
            savePath = Path.Combine(savePath, Path.GetFileNameWithoutExtension(sourcePackage));

            // Same name xbox issue fix
            if (packagePlatform.platform == GamePlatform.XBox360)
                savePath += GamePlatform.XBox360.ToString();
            DirectoryExtension.Move(unpackedDir, savePath, true);
            unpackedDir = savePath;

            // REORGANIZE
            var structured = ConfigRepository.Instance().GetBoolean("creator_structured");
            if (structured && CurrentGameVersion == GameVersion.RS2014)
                unpackedDir = DLCPackageData.DoLikeProject(savePath);

            // LOAD DATA
            DLCPackageData info = null;  // DLCPackageData specific to RS2
            if (CurrentGameVersion == GameVersion.RS2014)
                info = DLCPackageData.LoadFromFolder(unpackedDir, packagePlatform, packagePlatform);
            else
                info = DLCPackageData.RS1LoadFromFolder(unpackedDir, packagePlatform, rbConvert.Checked);

            switch (packagePlatform.platform)
            {
                case GamePlatform.Pc:
                    info.Pc = true;
                    break;
                case GamePlatform.Mac:
                    info.Mac = true;
                    break;
                case GamePlatform.XBox360:
                    info.XBox360 = true;
                    break;
                case GamePlatform.PS3:
                    info.PS3 = true;
                    break;
            }

            // FILL PACKAGE CREATOR FORM
            FillPackageCreatorForm(info, unpackedDir);

            // TODO: this code is depreicated for now
            // AUTO SAVE DLC TEMPLATE
            //SaveTemplateFile(unpackedDir);
            //Application.DoEvents();
            //MessageBox.Show(CurrentRocksmithTitle + " DLC Template was imported.", MESSAGEBOX_CAPTION, MessageBoxButtons.OK, MessageBoxIcon.Information);

            Parent.Focus();
        }


        /// <summary>
        /// Fixes B Standard and below tuning issues for bass. 
        /// This is done by lowering the pitch down to 220Hz, and raising the tuning offset one octave up(+12 steps).
        /// This function is meant for fast and easy "one click" fixes to packed CDLC.
        /// </summary>
        /// <remarks>Routine:
        /// The CDLC will be unpacked, edited, and then repacked with '_bassfixed' appended to the file name.
        /// </remarks>
        /// <param name="sender">Sender.</param>
        /// <param name="e">E.</param>
        /// <param name="lowTuningBassFixButton">Low tuning bass fix button.</param>
        /// <param name="quick">chooses directory of source file, uses source file name with "_bassfix" appended.</param>
        /// <param name="deleteSourceFile">Deletes source file after extracting needed information and generating the file.</param>
        public void dlcLowTuningBassFix(object sender, EventArgs e, Button lowTuningBassFixButton, Boolean quick = false, Boolean deleteSourceFile = false)
        {
            string sourcePackage;
            string saveWorkingDirectoryPath;

            lowTuningBassFixButton.Enabled = false;

            // GET PATH
            using (var ofd = new OpenFileDialog())
            {
                ofd.Title = "Select one DLC to import";
                ofd.Filter = CurrentOFDPackageFilter;

                if (ofd.ShowDialog() != DialogResult.OK)
                {
                    lowTuningBassFixButton.Enabled = true;
                    return;
                }
                sourcePackage = ofd.FileName;
            }

            if (!sourcePackage.IsValidPSARC())
            {
                MessageBox.Show(String.Format("File '{0}' isn't valid. File extension was changed to '.invalid'", Path.GetFileName(sourcePackage)), MESSAGEBOX_CAPTION, MessageBoxButtons.OK, MessageBoxIcon.Error);
                return;
            }

            if (!quick)
            {
                using (var fbd = new VistaFolderBrowserDialog())
                {
                    fbd.Description = "Select folder to save project artifacts";
                    fbd.UseDescriptionForTitle = true;

                    if (fbd.ShowDialog() != DialogResult.OK)
                    {
                        lowTuningBassFixButton.Enabled = true;
                        return;
                    }
                    saveWorkingDirectoryPath = fbd.SelectedPath;
                }
            }
            else
            {
                saveWorkingDirectoryPath = Path.GetDirectoryName(sourcePackage);
            }

            // UNPACK
            var unpackedDir = Packer.Unpack(sourcePackage, saveWorkingDirectoryPath, true, true, false);
            var packagePlatform = sourcePackage.GetPlatform();

            // REORGANIZE
            var structured = ConfigRepository.Instance().GetBoolean("creator_structured");
            if (structured && !quick)
                unpackedDir = DLCPackageData.DoLikeProject(unpackedDir);

            // LOAD DATA
            var info = DLCPackageData.LoadFromFolder(unpackedDir, packagePlatform);

            switch (packagePlatform.platform)
            {
                case GamePlatform.Pc:
                    info.Pc = true;
                    break;
                case GamePlatform.Mac:
                    info.Mac = true;
                    break;
                case GamePlatform.XBox360:
                    info.XBox360 = true;
                    break;
                case GamePlatform.PS3:
                    info.PS3 = true;
                    break;
            }

            //apply bass fix.
            for (int i = 0; i < info.Arrangements.Count; i++)
            {
                Arrangement arr = info.Arrangements[i];
                if (arr.ArrangementType == ArrangementType.Bass)
                {
                    ApplyBassFix(arr);
                }
            }

            if (!quick)
            {
                using (var ofd = new SaveFileDialog())
                {
                    ofd.FileName = GeneralExtensions.GetShortName("{0}_{1}_v{2}", ArtistSort, SongTitleSort, PackageVersion.Replace(".", "_"), ConfigRepository.Instance().GetBoolean("creator_useacronyms"));
                    ofd.Filter = CurrentRocksmithTitle + " DLC (*.*)|*.*";
                    if (ofd.ShowDialog() != DialogResult.OK)
                    {
                        lowTuningBassFixButton.Enabled = true;
                        return;
                    }
                    dlcSavePath = ofd.FileName;
                }
            }
            else
            {
                dlcSavePath = Path.Combine(saveWorkingDirectoryPath, Path.GetFileNameWithoutExtension(sourcePackage) + "_bassfix");
            }

            if (Path.GetFileName(dlcSavePath).Contains(" ") && platformPS3.Checked)
                if (!ConfigRepository.Instance().GetBoolean("creator_ps3pkgnamewarn"))
                {
                    MessageBox.Show(String.Format("PS3 package name can't support space character due to encryption limitation. {0} Spaces will be automatic removed for your PS3 package name.", Environment.NewLine), MESSAGEBOX_CAPTION, MessageBoxButtons.OK, MessageBoxIcon.Warning);
                }
                else
                {
                    ConfigRepository.Instance()["creator_ps3pkgnamewarn"] = true.ToString();
                }

            if (deleteSourceFile)
            {
                try
                {
                    File.Delete(sourcePackage);
                }
                catch (Exception ex)
                {
                    Console.Write(ex.Message);
                    MessageBox.Show("Denied rights needed to delete source package, or an error occured. Package still may exist. Try running as Administrator.");
                }
            }

            if (!bwGenerate.IsBusy && info != null)
            {// Generate CDLC
                bwGenerate.RunWorkerAsync(info);
            }
        }
        //consider to move this to RocksmithToolkitLib
        public void ApplyBassFix(Arrangement arr)
        {
            if (arr.TuningPitch.Equals(220.0))
            {
                MessageBox.Show("This song is already at 220Hz pitch (bass fixed applied already?)", MESSAGEBOX_CAPTION, MessageBoxButtons.OK, MessageBoxIcon.Error);
                return;
            }
            Song2014 songXml = Song2014.LoadFromFile(arr.SongXml.File);
            // Force 220Hz
            arr.TuningPitch = 220.0;
            songXml.CentOffset = "-1200.0";
            // Octave up for each string
            var strings = arr.TuningStrings.ToShortArray();
            for (int s = 0; s < strings.Length; s++)
            {
                if (strings[s] != 0)
                    strings[s] += 12;
            }
            //Detect tuning
            var tuning = TuningDefinitionRepository.Instance().SelectAny(new TuningStrings(strings), CurrentGameVersion);
            if (tuning == null)
            {
                tuning = new TuningDefinition();
                tuning.Tuning = new TuningStrings(strings);
                tuning.UIName = tuning.Name = tuning.NameFromStrings(tuning.Tuning, true, false);
                tuning.Custom = true;
                tuning.GameVersion = GameVersion.RS2014;
                TuningDefinitionRepository.Instance().Add(tuning, true);
            }
            arr.TuningStrings = tuning.Tuning;
            arr.Tuning = tuning.Name;
            songXml.Tuning = tuning.Tuning;
            using (var stream = File.OpenWrite(arr.SongXml.File))
            {
                songXml.Serialize(stream);
            }
        }

        private void FillPackageCreatorForm(DLCPackageData info, string filesBaseDir)
        {
            platformPC.Checked = info.Pc;
            platformMAC.Checked = info.Mac;
            platformXBox360.Checked = info.XBox360;
            platformPS3.Checked = info.PS3;

            PackageVersion = info.PackageVersion;

            tonesLB.Items.Clear();
            switch (CurrentGameVersion)
            {
                case GameVersion.RS2012:
                    if (info.Tones == null)
                        info.Tones = new List<Tone>();
                    if (info.Tones.Count == 0)
                        info.Tones.Add(CreateNewTone());

                    foreach (var tone in info.Tones)
                    {
                        if (String.IsNullOrEmpty(tone.Key))
                            tone.Key = tone.Name.GetValidName();

                        tonesLB.Items.Add(tone);
                    }
                    break;
                case GameVersion.None:
                case GameVersion.RS2014:
                    if (info.TonesRS2014 == null)
                        info.TonesRS2014 = new List<Tone2014>();
                    if (info.TonesRS2014.Count == 0)
                        info.TonesRS2014.Add(CreateNewTone());

                    foreach (var toneRS2014 in info.TonesRS2014)
                    {
                        if (String.IsNullOrEmpty(toneRS2014.Key))
                            toneRS2014.Key = toneRS2014.Name.GetValidName();

                        tonesLB.Items.Add(toneRS2014);
                    }
                    break;
            }

            var BasePath = Path.GetDirectoryName(filesBaseDir);

            // Song INFO
            // TODO: add routemask (lead, rhythum, bass, none
            DlcNameTB.Text = info.Name;

            PopulateAppIdCombo();
            Application.DoEvents();
            AppIdTB.Text = info.AppId;
            SelectComboAppId(info.AppId);
            if (String.IsNullOrEmpty(AppIdTB.Text))
                AppIdTB.Text = "248750"; //hardcoded for now
            AlbumTB.Text = info.SongInfo.Album;
            SongDisplayNameTB.Text = info.SongInfo.SongDisplayName;
            SongDisplayNameSortTB.Text = info.SongInfo.SongDisplayNameSort;
            YearTB.Text = info.SongInfo.SongYear.ToString();
            ArtistTB.Text = info.SongInfo.Artist;
            ArtistSortTB.Text = info.SongInfo.ArtistSort;
            AverageTempoTB.Text = info.SongInfo.AverageTempo.ToString();

            // Album art
            AlbumArtPath = info.AlbumArtPath.AbsoluteTo(BasePath);

            // Lyric art
            if (!String.IsNullOrEmpty(info.LyricArtPath))
                LyricArtPath = info.LyricArtPath.AbsoluteTo(BasePath);

            // Audio file
            if (!String.IsNullOrEmpty(info.OggPath)) //TODO: empty since last commit
                AudioPath = info.OggPath.AbsoluteTo(BasePath);
            platformPC.Checked = !String.IsNullOrEmpty(info.OggPath);

            songVolumeBox.Value = Decimal.Round((decimal)info.Volume, 2);
            previewVolumeBox.Value = (info.PreviewVolume != null) ? Decimal.Round((decimal)info.PreviewVolume, 2) : songVolumeBox.Value;

            //if (platformXBox360.Checked)
            //    rbuttonSignatureLIVE.Checked = info.SignatureType == PackageMagic.LIVE;

            arrangementLB.Items.Clear();
            foreach (var arrangement in info.Arrangements)
            {
                arrangement.SongXml.File = arrangement.SongXml.File.AbsoluteTo(BasePath);

                if (!String.IsNullOrEmpty(arrangement.FontSng))
                    arrangement.FontSng = arrangement.FontSng.AbsoluteTo(BasePath);

                arrangement.CleanCache();

                if (arrangement.Metronome == Metronome.Itself)
                    continue;
                if (arrangement.ToneBase == null)
                {
                    switch (CurrentGameVersion)
                    {
                        case GameVersion.RS2012:
                            arrangement.ToneBase = info.Tones[0].Name;
                            break;
                        case GameVersion.None:
                        case GameVersion.RS2014:
                            arrangement.ToneBase = info.TonesRS2014[0].Name;
                            break;
                    }
                }
                if (arrangement.ArrangementType != ArrangementType.Vocal)
                {
                    // Populate tuning info
                    try
                    {
                        var songXml = Song2014.LoadFromFile(arrangement.SongXml.File);
                        arrangement.CapoFret = songXml.Capo;
                        //Load tuning from Arrangement
                        var tuning = TuningDefinitionRepository.Instance().SelectAny(arrangement.TuningStrings, CurrentGameVersion);
                        if (tuning == null)
                        {
                            tuning = new TuningDefinition();
                            tuning.Tuning = arrangement.TuningStrings;
                            tuning.Custom = true;
                            tuning.GameVersion = CurrentGameVersion;
                            tuning.Name = tuning.UIName = arrangement.Tuning;
                            if (String.IsNullOrEmpty(tuning.Name))
                            {
                                tuning.Name = tuning.UIName = tuning.NameFromStrings(arrangement.TuningStrings, arrangement.ArrangementType == ArrangementType.Bass);
                            }

                            TuningDefinitionRepository.Instance().Add(tuning, true);
                        }
                        // Populate Arrangement tuning info
                        arrangement.Tuning = tuning.UIName;
                        arrangement.TuningStrings = tuning.Tuning;
                        //Cleanup
                        tuning = null;
                        songXml = null;
                    }
                    catch
                    {
                        /* Handle old types of *.dlc.xml */
                    }
                }

                arrangementLB.Items.Add(arrangement);
            }
           
            userChanges = CurrentGameVersion == GameVersion.None ? 1 : 0;
        }

        private void songVolumeBox_ValueChanged(object sender, EventArgs e)
        {
            previewVolumeBox.Value = songVolumeBox.Value;
        }

        private void songVolumeBox_MouseEnter(object sender, EventArgs e)
        {
            ToolTip tt = new ToolTip();
            tt.IsBalloon = true;
            tt.InitialDelay = 0;
            tt.ShowAlways = true;
            Control control = (Control)sender;
            string name = control.Name;
            if (name == "songVolumeBox")
                tt.SetToolTip(songVolumeBox, "HIGHER 0,-1,-2,-3,..., AVERAGE -12 ,...,-16,-17 LOWER");
            else
                tt.SetToolTip(previewVolumeBox, "HIGHER 0,-1,-2,-3,..., AVERAGE -12 ,...,-16,-17 LOWER");
        }

        private DLCPackageData GetPackageData()
        {
            if (CurrentGameVersion != GameVersion.RS2012)
            {
                if (!platformPC.Checked && !platformMAC.Checked && !platformXBox360.Checked && !platformPS3.Checked)
                {
                    MessageBox.Show("Error: No game platform selected", MESSAGEBOX_CAPTION, MessageBoxButtons.OK, MessageBoxIcon.Error);
                    return null;
                }
            }
            else
            {
                if (!platformPC.Checked && !platformXBox360.Checked && !platformPS3.Checked)
                {
                    MessageBox.Show("Error: No game platform selected", MESSAGEBOX_CAPTION, MessageBoxButtons.OK, MessageBoxIcon.Error);
                    return null;
                }
            }
            int year, tempo;
            if (String.IsNullOrEmpty(DLCName))
            {
                DlcNameTB.Focus();
                return null;
            }
            if (DLCName == SongTitle)
            {
                MessageBox.Show("Error: DLC name can't be the same of song name", MESSAGEBOX_CAPTION, MessageBoxButtons.OK, MessageBoxIcon.Error);
                DlcNameTB.Focus();
                return null;
            }
            if (String.IsNullOrEmpty(SongTitle))
            {
                SongDisplayNameTB.Focus();
                return null;
            }
            if (string.IsNullOrEmpty(Album))
            {
                AlbumTB.Focus();
                return null;
            }
            if (String.IsNullOrEmpty(Artist))
            {
                ArtistTB.Focus();
                return null;
            }
            if (!Int32.TryParse(AlbumYear, out year))
            {
                YearTB.Focus();
                return null;
            }
            if (!Int32.TryParse(AverageTempo, out tempo))
            {
                AverageTempoTB.Focus();
                return null;
            }
            if (String.IsNullOrEmpty(AppId))
            {
                AppIdTB.Focus();
                return null;
            }
            if (String.IsNullOrEmpty(PackageVersion))
            {
                PackageVersion = "1";
            }
            if (!PackageVersion.Equals(PackageVersion.GetValidVersion()))
            {
                MessageBox.Show(String.Format("Package verion field contain invalid characters!\n" +
                                              "Please replace this: {0}\n" +
                                              "By something like this: 1 or 2.1 or 2.2.1",
                                              PackageVersion), MESSAGEBOX_CAPTION, MessageBoxButtons.OK, MessageBoxIcon.Error);
                packageVersionTB.Focus();
                return null;
            }

            //Album Art validation (alert only)
            if (String.IsNullOrEmpty(AlbumArtPath) || !File.Exists(AlbumArtPath))
            {
                var diagResult = MessageBox.
                    Show("Warning: Album Art file not found!" + Environment.NewLine +
                    "If you click 'Yes' default album art will be defined." + Environment.NewLine +
                    "Else you click 'No' you want to select the Album Art File.",
                    MESSAGEBOX_CAPTION, MessageBoxButtons.YesNo, MessageBoxIcon.Error);

                if (diagResult == DialogResult.No)
                {
                    albumArtPathTB.Focus();
                    return null;
                }
            }

            if (!File.Exists(AudioPath))
            {
                audioPathTB.Focus();
                return null;
            }

            try
            {
                if (CurrentGameVersion == GameVersion.RS2012 || Path.GetExtension(AudioPath) == "*.wem")
                    AudioPath.VerifyHeaders();
            }
            catch (InvalidDataException ex)
            {
                MessageBox.Show(ex.Message, MESSAGEBOX_CAPTION, MessageBoxButtons.OK, MessageBoxIcon.Error);
                return null;
            }

            string audioPreviewPath = null;

            if (CurrentGameVersion != GameVersion.RS2012)
            {
                ExternalApps.VerifyExternalApps(); // for testing
                var audioPathNoExt = Path.Combine(Path.GetDirectoryName(AudioPath), Path.GetFileNameWithoutExtension(AudioPath));
                var oggPath = String.Format(audioPathNoExt + ".ogg");
                var wavPath = String.Format(audioPathNoExt + ".wav");
                var wemPath = String.Format(audioPathNoExt + ".wem");
                var oggPreviewPath = String.Format(audioPathNoExt + "_preview.ogg");
                var wavPreviewPath = String.Format(audioPathNoExt + "_preview.wav");
                var wemPreviewPath = String.Format(audioPathNoExt + "_preview.wem");
                audioPreviewPath = wemPreviewPath;

                if (AudioPath.Substring(AudioPath.Length - 4).ToLower() == ".ogg")//RS1 old ogg was actually wwise
                {
                    ExternalApps.Ogg2Wav(AudioPath, wavPath);
                    if (!File.Exists(oggPreviewPath))
                    {
                        ExternalApps.Ogg2Preview(AudioPath, oggPreviewPath);
                        ExternalApps.Ogg2Wav(oggPreviewPath, wavPreviewPath);
                    }
                    AudioPath = wavPath;
                }

                if (AudioPath.Substring(AudioPath.Length - 4).ToLower() == ".wav")
                {
                    if (!File.Exists(wavPreviewPath))
                    {
                        ExternalApps.Wav2Ogg(AudioPath, oggPath, 4);
                        ExternalApps.Ogg2Preview(oggPath, oggPreviewPath);
                        ExternalApps.Ogg2Wav(oggPreviewPath, wavPreviewPath);
                    }
                    Wwise.Convert2Wem(AudioPath, wemPath, (int)audioQualityBox.Value);
                    AudioPath = wemPath;
                }

                if (AudioPath.Substring(AudioPath.Length - 4).ToLower() == ".wem" && !File.Exists(wemPreviewPath))
                {//TODO: test if no Wwise
                    OggFile.ConvertAudioPlatform(AudioPath, oggPath);
                    ExternalApps.Ogg2Wav(oggPath, wavPath);
                    ExternalApps.Ogg2Preview(oggPath, oggPreviewPath);
                    ExternalApps.Ogg2Wav(oggPreviewPath, wavPreviewPath);
                    Wwise.Convert2Wem(wavPath, wemPath, (int)audioQualityBox.Value);
                    AudioPath = wemPath;
                }
            }

            var arrangements = arrangementLB.Items.OfType<Arrangement>().ToList();
            foreach (var arr in arrangements)
            {
                if (!File.Exists(arr.SongXml.File))
                {
                    MessageBox.Show("Error: Song Xml File doesn't exist: " + arr.SongXml.File, MESSAGEBOX_CAPTION, MessageBoxButtons.OK, MessageBoxIcon.Error);
                    return null;
                }
                arr.SongFile.File = "";

            }

            if (arrangements.Count(x => x.ArrangementType == ArrangementType.Vocal && x.Name == ArrangementName.Vocals) > 1)
            {
                MessageBox.Show("Error: Multiple Vocals arrangement found", MESSAGEBOX_CAPTION, MessageBoxButtons.OK, MessageBoxIcon.Error);
                return null;
            }
            if (arrangements.Count(x => x.ArrangementType == ArrangementType.Vocal && x.Name == ArrangementName.JVocals) > 1)
            {
                MessageBox.Show("Error: Multiple JVocals arrangement found", MESSAGEBOX_CAPTION, MessageBoxButtons.OK, MessageBoxIcon.Error);
                return null;
            }

            var tones = new List<Tone>();
            if (CurrentGameVersion == GameVersion.RS2012)
                tones = tonesLB.Items.OfType<Tone>().ToList();

            var tonesRS2014 = new List<Tone2014>();
            if (CurrentGameVersion != GameVersion.RS2012)
                tonesRS2014 = tonesLB.Items.OfType<Tone2014>().ToList();

            //string liveSignatureID = xboxLicense0IDTB.Text.Trim();
            //if (rbuttonSignatureLIVE.Checked && String.IsNullOrEmpty(liveSignatureID))
            //{
            //    MessageBox.Show("Error: If LIVE signature is selected, your LIVE signature ID is required (in HEX format)", MESSAGEBOX_CAPTION, MessageBoxButtons.OK, MessageBoxIcon.Error);
            //    xboxLicense0IDTB.Focus();
            //    return null;
            //}
            //if (rbuttonSignatureLIVE.Checked && !new Regex("([A-Fa-f0-9]{2})+$").IsMatch(liveSignatureID))
            //{
            //    MessageBox.Show("Error: LIVE signature ID seems to be not valid, need a HEX value", MESSAGEBOX_CAPTION, MessageBoxButtons.OK, MessageBoxIcon.Error);
            //    xboxLicense0IDTB.Focus();
            //    return null;
            //}

            //List<XBox360License> licenses = new List<XBox360License>();
            //if (rbuttonSignatureLIVE.Checked)
            //{
            //    licenses.Add(new XBox360License() { ID = Convert.ToInt64(xboxLicense0IDTB.Text.Trim(), 16), Bit = 1, Flag = 1 });
            //}

            var songVol = (float)songVolumeBox.Value;
            var previewVol = (!String.IsNullOrEmpty(audioPreviewPath)) ? (float)songVolumeBox.Value : songVol;
            var data = new DLCPackageData
                       {
                           GameVersion = CurrentGameVersion,
                           Pc = platformPC.Checked,
                           Mac = platformMAC.Checked,
                           XBox360 = platformXBox360.Checked,
                           PS3 = platformPS3.Checked,
                           Name = DlcNameTB.Text,
                           AppId = AppIdTB.Text,

                           SongInfo = new SongInfo
                           {
                               SongDisplayName = SongDisplayNameTB.Text,
                               SongDisplayNameSort = String.IsNullOrEmpty(SongDisplayNameSortTB.Text.Trim()) ? SongDisplayNameTB.Text : SongDisplayNameSortTB.Text,
                               Album = AlbumTB.Text,
                               SongYear = year,
                               Artist = ArtistTB.Text,
                               ArtistSort = String.IsNullOrEmpty(ArtistSortTB.Text.Trim()) ? ArtistTB.Text : ArtistSortTB.Text,
                               AverageTempo = tempo
                           },

                           AlbumArtPath = AlbumArtPath,
                           LyricArtPath = LyricArtPath,
                           OggPath = AudioPath,
                           OggPreviewPath = audioPreviewPath,
                           Arrangements = arrangements,
                           Tones = tones,
                           TonesRS2014 = tonesRS2014,
                           Volume = songVol,
                           PreviewVolume = previewVol,
                           SignatureType = PackageMagic.CON,
                           PackageVersion = PackageVersion.GetValidVersion()
                       };

            return data;
        }

        public void UpdateXml(Arrangement arr, DLCPackageData info)
        {
            // update xml with user modified DLCPackageData info                                               
            if (CurrentGameVersion == GameVersion.None || CurrentGameVersion == GameVersion.RS2014)
            {
                var songXml = Song2014.LoadFromFile(arr.SongXml.File);
                arr.SongFile = new RocksmithToolkitLib.DLCPackage.AggregateGraph.SongFile { File = "" };
                arr.Id = IdGenerator.Guid();
                arr.MasterId = RandomGenerator.NextInt();

                songXml.AlbumName = info.SongInfo.Album;
                songXml.AlbumYear = info.SongInfo.SongYear.ToString();
                songXml.ArtistName = info.SongInfo.Artist;
                songXml.ArtistNameSort = info.SongInfo.ArtistSort;
                songXml.AverageTempo = info.SongInfo.AverageTempo;
                songXml.Title = info.SongInfo.SongDisplayName;
                songXml.ToneBase = arr.ToneBase;
                songXml.ToneA = arr.ToneA;
                songXml.ToneB = arr.ToneB;
                songXml.ToneC = arr.ToneC;
                songXml.ToneD = arr.ToneD;

                using (var stream = File.OpenWrite(arr.SongXml.File))
                {
                    songXml.Serialize(stream);
                }
            }

            if (CurrentGameVersion == GameVersion.RS2012)
            {
                var songXml = Song.LoadFromFile(arr.SongXml.File);
                arr.SongFile = new RocksmithToolkitLib.DLCPackage.AggregateGraph.SongFile { File = "" };
                songXml.Title = info.SongInfo.SongDisplayName;

                using (var stream = File.OpenWrite(arr.SongXml.File))
                {
                    songXml.Serialize(stream);
                }
            }
        }

        public Arrangement GenMetronomeArr(Arrangement arr)
        {
            var mArr = GeneralExtensions.Copy<Arrangement>(arr);
            var songXml = Song2014.LoadFromFile(mArr.SongXml.File);
            var newXml = Path.GetTempFileName();
            mArr.SongXml = new RocksmithToolkitLib.DLCPackage.AggregateGraph.SongXML { File = newXml };
            mArr.SongFile = new RocksmithToolkitLib.DLCPackage.AggregateGraph.SongFile { File = "" };
            mArr.CleanCache();
            mArr.BonusArr = true;
            mArr.Id = IdGenerator.Guid();
            mArr.MasterId = RandomGenerator.NextInt();
            mArr.Metronome = Metronome.Itself;
            songXml.ArrangementProperties.Metronome = (int)Metronome.Itself;

            var ebeats = songXml.Ebeats;
            var songEvents = new RocksmithToolkitLib.Xml.SongEvent[ebeats.Length];
            for (var i = 0; i < ebeats.Length; i++)
            {
                songEvents[i] = new RocksmithToolkitLib.Xml.SongEvent
                {
                    Code = ebeats[i].Measure == -1 ? "B1" : "B0",
                    Time = ebeats[i].Time
                };
            }
            songXml.Events = songXml.Events.Union(songEvents, new EqSEvent()).OrderBy(x => x.Time).ToArray();
            using (var stream = File.OpenWrite(mArr.SongXml.File))
            {
                songXml.Serialize(stream);
            }

            return mArr;
        }

        public class EqSEvent : IEqualityComparer<RocksmithToolkitLib.Xml.SongEvent>
        {
            public bool Equals(RocksmithToolkitLib.Xml.SongEvent x, RocksmithToolkitLib.Xml.SongEvent y)
            {
                if (x == null)
                    return y == null;

                return x.Code == y.Code && x.Time.Equals(y.Time);
            }

            public int GetHashCode(RocksmithToolkitLib.Xml.SongEvent obj)
            {
                if (ReferenceEquals(obj, null))
                    return 0;
                return obj.Code.GetHashCode() | obj.Time.GetHashCode();
            }
        }
        private void cmbAppIds_SelectedValueChanged(object sender, EventArgs e)
        {
            if (cmbAppIds.SelectedItem != null)
            {
                AppIdTB.Text = ((SongAppId)cmbAppIds.SelectedItem).AppId;
            }
        }

        private void ArrangementLB_MouseDoubleClick(object sender, MouseEventArgs e)
        {
            arrangementEditButton_Click(sender, e);
        }

        private void arrangementEditButton_Click(object sender, EventArgs e)
        {
            if (arrangementLB.SelectedItem != null)
            {
                var arrangement = (Arrangement)arrangementLB.SelectedItem;
                using (var form = new ArrangementForm(arrangement, this, CurrentGameVersion) { Text = "Edit Arrangement" })
                {
                    if (DialogResult.OK != form.ShowDialog())
                    {
                        return;
                    }
                }
                arrangementLB.Items[arrangementLB.SelectedIndex] = arrangement;
            }
        }

        public void toneAddButton_Click(object sender = null, EventArgs e = null)
        {
            var tone = CreateNewTone();
            using (var form = new ToneForm())
            {
                var currentGameVersion = CurrentGameVersion != GameVersion.RS2012 ? GameVersion.RS2014 : GameVersion.RS2012;
                form.CurrentGameVersion = currentGameVersion;
                form.toneControl1.CurrentGameVersion = currentGameVersion;
                form.toneControl1.Init();
                form.toneControl1.Tone = GeneralExtensions.Copy(tone);
                form.ShowDialog();

                if (form.Saved)
                    tonesLB.Items.Add(form.toneControl1.Tone);
            }
        }

        private void toneRemoveButton_Click(object sender, EventArgs e)
        {
            RemoveTone();
        }

        private void RemoveTone()
        {
            if (MessageBox.Show("Are you sure to delete the selected tone?", MESSAGEBOX_CAPTION, MessageBoxButtons.YesNo, MessageBoxIcon.Warning) == DialogResult.No)
                return;

            if (tonesLB.SelectedItem != null && tonesLB.Items.Count > 1)
            {
                dynamic tone = tonesLB.SelectedItem;
                tonesLB.Items.Remove(tonesLB.SelectedItem);

                dynamic firstTone = tonesLB.Items[0];
                foreach (var item in arrangementLB.Items.OfType<Arrangement>())
                    if (tone.Name.Equals(item.ToneBase) && String.IsNullOrEmpty(item.ToneB))
                        item.ToneBase = firstTone.Name;
                arrangementLB.Refresh();
            }
        }

        private void toneDuplicateButton_Click(object sender, EventArgs e)
        {
            DuplicateTone();
        }

        private void DuplicateTone()
        {
            if (tonesLB.SelectedItem != null && tonesLB.Items.Count > 0)
            {
                dynamic tone = tonesLB.SelectedItem;
                switch (CurrentGameVersion)
                {
                    case GameVersion.RS2012:
                        tone = GeneralExtensions.Copy<Tone>((Tone)tonesLB.SelectedItem);
                        break;
                    case GameVersion.None:
                    case GameVersion.RS2014:
                        tone = GeneralExtensions.Copy<Tone2014>((Tone2014)tonesLB.SelectedItem);
                        break;
                }
                var name = GetUniqueToneName(tonesLB.Text);
                tone.Name = name;
                tone.Key = name;
                tonesLB.Items.Add(tone);

                dynamic firstTone = tonesLB.Items[0];
                foreach (var item in arrangementLB.Items.OfType<Arrangement>())
                    if (tone.Name.Equals(item.ToneBase))
                        item.ToneBase = firstTone.Name;
                arrangementLB.Refresh();
            }
        }

        private void ToneLB_MouseDoubleClick(object sender, MouseEventArgs e)
        {
            toneEditButton_Click(sender, e);
        }

        private void toneEditButton_Click(object sender, EventArgs e)
        {
            if (tonesLB.SelectedItem != null)
            {
                dynamic tone = tonesLB.SelectedItem;
                var toneName = tone.Name;
                using (var form = new ToneForm())
                {
                    var currentGameVersion = CurrentGameVersion != GameVersion.RS2012 ? GameVersion.RS2014 : GameVersion.RS2012;
                    form.CurrentGameVersion = currentGameVersion;
                    form.toneControl1.CurrentGameVersion = currentGameVersion;
                    form.toneControl1.Init();
                    form.toneControl1.Tone = GeneralExtensions.Copy(tone);
                    form.ShowDialog();

                    if (form.Saved)
                        tonesLB.Items[tonesLB.SelectedIndex] = form.toneControl1.Tone;
                }
                if (toneName != tone.Name)
                {
                    // Update tone slots if name are changed
                    for (int i = 0; i < arrangementLB.Items.Count; i++)
                    {
                        var arrangement = (Arrangement)arrangementLB.Items[i];
                        var toneSlotsAffected = false;

                        if (toneName.Equals(arrangement.ToneBase))
                        {
                            arrangement.ToneBase = tone.Name;
                            if (CurrentGameVersion != GameVersion.RS2012)
                                arrangement.ToneA = tone.Name;
                        }
                        if (CurrentGameVersion != GameVersion.RS2012)
                        {
                            if (toneName.Equals(arrangement.ToneB))
                                arrangement.ToneB = tone.Name;
                            if (toneName.Equals(arrangement.ToneC))
                                arrangement.ToneC = tone.Name;
                            if (toneName.Equals(arrangement.ToneD))
                                arrangement.ToneD = tone.Name;
                        }

                        if (toneSlotsAffected)
                            arrangementLB.Items[i] = arrangement;
                    }
                }
            }
        }

        private void toneImportButton_Click(object sender, EventArgs e)
        {
            string toneImportFile;
            using (var ofd = new OpenFileDialog())
            {
                ofd.Title = "Select DLC Song Package or Tone File or your Profile";
                ofd.Filter = CurrentOFDToneImportFilter;
                if (ofd.ShowDialog() != DialogResult.OK) return;
                toneImportFile = ofd.FileName;
            }

            ImportTone(toneImportFile);
        }

        private void ImportTone(string toneImportFile)
        {
            try
            {
                Application.DoEvents();
                if (CurrentGameVersion != GameVersion.RS2012)
                {
                    List<Tone2014> tones2014 = Tone2014.Import(toneImportFile);
                    //Popup ToneImportForm if tones > 1
                    if (tones2014.Count > 1)
                        using (var importForm = new ToneImportForm())
                        {
                            importForm.Tone2014 = tones2014;
                            importForm.PopList();
                            if (importForm.ShowDialog() != DialogResult.OK)
                                return;
                            foreach (var tone in importForm.Tone2014.Where(t => !t.GearList.IsNull()))
                                tonesLB.Items.Add(tone);
                        }
                    else if (tones2014.Count > 0) tonesLB.Items.Add(tones2014.FirstOrDefault(t => !t.GearList.IsNull()));
                }
                else
                {
                    List<Tone> tones = Tone.Import(toneImportFile);
                    //Popup ToneImportForm if tones > 1
                    if (tones.Count > 1)
                        using (var importForm = new ToneImportForm())
                        {
                            importForm.Tone = tones;
                            importForm.PopList();
                            if (importForm.ShowDialog() != DialogResult.OK)
                                return;
                            foreach (var tone in importForm.Tone.Where(t => t.PedalList.Count != 0))
                                tonesLB.Items.Add(tone);
                        }
                    else if (tones.Count > 0) tonesLB.Items.Add(tones.FirstOrDefault(t => t.PedalList.Count != 0));
                }
            }
            catch (Exception ex)
            {
                MessageBox.Show("Can't import tone(s). \n" + ex.Message, MESSAGEBOX_CAPTION, MessageBoxButtons.OK, MessageBoxIcon.Error);
                return;
            }

            MessageBox.Show("Tone(s) was imported.", "DLC Package Creator", MessageBoxButtons.OK, MessageBoxIcon.Information);
            Parent.Focus();
        }

        private void plataform_CheckedChanged(object sender, EventArgs e)
        {
            if (platformPC.Checked || platformMAC.Checked)
            {
                AppIdTB.Enabled = true;
                cmbAppIds.Enabled = true;
            }
            else if (!platformPC.Checked && !platformMAC.Checked)
            {
                AppIdTB.Enabled = false;
                cmbAppIds.Enabled = false;
            }
            GameVersion_CheckedChanged(null, null);
        }

        private void AppIdTB_TextChanged(object sender, EventArgs e)
        {
            var appId = ((TextBox)sender).Text.Trim();
            AppIdTB.TextChanged -= AppIdTB_TextChanged;
            SelectComboAppId(appId);
            AppIdTB.TextChanged += AppIdTB_TextChanged;
        }

        private void SelectComboAppId(string appId)
        {
            var songAppId = SongAppIdRepository.Instance().Select(appId, CurrentGameVersion);
            if (SongAppIdRepository.Instance().List.Any<SongAppId>(a => a.AppId == appId))
                cmbAppIds.SelectedItem = songAppId;
        }

        private void DlcNameTB_Leave(object sender, EventArgs e)
        {
            TextBox dlcName = (TextBox)sender;
            dlcName.Text = dlcName.Text.Trim().GetValidSongName(SongTitle);
        }

        private void AverageTempoTB_Leave(object sender, EventArgs e)
        {
            TextBox averageTB = (TextBox)sender;
            float tempo = 0;
            float.TryParse(averageTB.Text.Trim(), out tempo);
            averageTB.Text = Math.Round(tempo).ToString();
        }

        //private void rbuttonSignature_CheckedChanged(object sender, EventArgs e)
        //{
        //    xboxLicense0IDTB.Visible = rbuttonSignatureLIVE.Checked;
        //}

        private void ListBox_KeyDown(object sender, KeyEventArgs e)
        {
            var control = (ListBox)sender;

            object item = control.SelectedItem;
            int index = control.SelectedIndex;
            int newIndex = index;

            switch ((Keys)e.KeyValue)
            {
                case Keys.Up:
                case Keys.PageUp:
                    newIndex--;
                    break;
                case Keys.Down:
                case Keys.PageDown:
                    newIndex++;
                    break;
                case Keys.D:
                    DuplicateTone();
                    break;
                case Keys.Delete:
                    RemoveTone();
                    return;
                default:
                    return;
            }

            control.Items.RemoveAt(index);

            if (newIndex >= 0 && newIndex <= control.Items.Count)
            {
                control.Items.Insert(newIndex, item);
                control.SelectedIndex = index;
            }
            else
            {
                control.Items.Insert(index, item);
                control.SelectedIndex = index;
            }

            control.Refresh();
        }

        private void GameVersion_CheckedChanged(object sender, EventArgs e)
        {
            audioPathTB.Text = "";
            PopulateAppIdCombo();
            PopulateTonesLB();

            // MAC RS2014 only
            platformMAC.Enabled = CurrentGameVersion != GameVersion.RS2012;

            // Song preview volume RS2014 only
            previewVolumeBox.Enabled = CurrentGameVersion != GameVersion.RS2012;

            // AudioTB
            switch (CurrentGameVersion)
            {
                case GameVersion.None:
                case GameVersion.RS2014:
                    audioQualityBox.Enabled = true;

                    if (!platformMAC.Checked && !platformPS3.Checked && !platformXBox360.Checked)
                    {
                        audioPathTB.Cue = "Audio to Wwise 2013 converter for Windows (*.wem, *.ogg, *.wav)";
                        label2.Text = @"Song preview is generated automatically if not provided in format 'filename_preview.wem'";
                    }
                    else
                    {
                        audioPathTB.Cue = "Converted audio on Wwise 2013 for Windows, Mac, XBox360 or PS3 (*.wem)";
                        label2.Text = @"Song preview must have the same file name with '_preview' in the end, eg. 'filename_preview.wem'";
                    }
                    break;

                default:
                    platformMAC.Checked = false;
                    audioQualityBox.Enabled = false;
                    audioPathTB.Cue = "Converted audio on Wwise 2010 for Windows, XBox360 or PS3 (*.ogg)";
                    label2.Text = "";
                    break;
            }

            // Import Package
            //  dlcImportButton.Enabled = CurrentGameVersion == GameVersion.RS2014;
        }

        private void PopulateAppIdCombo()
        {
            // takes into account possible conversion
            GameVersion currentGameVersion = GameVersion.RS2014;
            if (CurrentGameVersion == GameVersion.RS2012)
                currentGameVersion = GameVersion.RS2012;

            cmbAppIds.Items.Clear();
            foreach (var song in SongAppIdRepository.Instance().Select(currentGameVersion))
                cmbAppIds.Items.Add(song);

            var songAppId = SongAppIdRepository.Instance().Select((currentGameVersion == GameVersion.RS2014) ? ConfigRepository.Instance()["general_defaultappid_RS2014"] : ConfigRepository.Instance()["general_defaultappid_RS2012"], currentGameVersion);
            cmbAppIds.SelectedItem = songAppId;
            AppId = songAppId.AppId;
        }

        private void ProgressChanged(object sender, ProgressChangedEventArgs e)
        {
            if (e.ProgressPercentage <= updateProgress.Maximum)
                updateProgress.Value = e.ProgressPercentage;
            else
                updateProgress.Value = updateProgress.Maximum;

            ShowCurrentOperation(e.UserState as string);
        }

        private void ProcessCompleted(object sender, RunWorkerCompletedEventArgs e)
        {
            switch (Convert.ToString(e.Result))
            {
                case "generate":
                    var message = "Package was generated.";
                    if (errorsFound.Length > 0)
                        message = String.Format("Package was generated with errors! See below: {0}{1}", Environment.NewLine, errorsFound);
                    message += String.Format("{0}You want to open the folder in which the package was generated?{0}", Environment.NewLine);
                    if (MessageBox.Show(message, MESSAGEBOX_CAPTION, MessageBoxButtons.YesNo, MessageBoxIcon.Information) == DialogResult.Yes)
                    {
                        Process.Start(Path.GetDirectoryName(dlcSavePath));
                    }
                    break;
                case "error":
                    var message2 = String.Format("Package generation failed. See below: {0}{1}{0}", Environment.NewLine, errorsFound);
                    MessageBox.Show(message2, MESSAGEBOX_CAPTION, MessageBoxButtons.OK, MessageBoxIcon.Error);
                    Parent.Focus();
                    break;
            }

            dlcGenerateButton.Enabled = true;
            updateProgress.Visible = false;
            currentOperationLabel.Visible = false;
        }

        private void ShowCurrentOperation(string message)
        {
            currentOperationLabel.Text = message;
            currentOperationLabel.Refresh();
        }

        private void ArtistSortTB_TextChanged(object sender, EventArgs e)
        {
            ArtistSortTB.TextChanged -= ArtistSortTB_TextChanged;
            var artist = ArtistSortTB.Text.ToUpperInvariant();
            if (artist.StartsWith("THE ", StringComparison.Ordinal))
            {
                ArtistSortTB.Text = artist.GetValidSortName();
            }
            ArtistSortTB.TextChanged += ArtistSortTB_TextChanged;
        }

        private void packageVersionTB_KeyPress(object sender, KeyPressEventArgs e)
        {
            if (!char.IsDigit(e.KeyChar) && e.KeyChar != Char.Parse(".") && e.KeyChar != (int)Keys.Back)
                e.Handled = true;
        }

        private void audioQualityBox_MouseEnter(object sender, EventArgs e)
        {
            ToolTip tt = new ToolTip();
            tt.IsBalloon = true;
            tt.InitialDelay = 0;
            tt.ShowAlways = true;
            tt.SetToolTip(audioQualityBox, "HIGH QUALITY 9 ... DEFAULT 4" +
                Environment.NewLine + "Leave audio quality set to default 4" +
                Environment.NewLine + "if source audio quality is unknown or low  ");

        }

        private void rbConvert_MouseEnter(object sender, EventArgs e)
        {
            ToolTip tt = new ToolTip();
            tt.IsBalloon = true;
            tt.InitialDelay = 0;
            tt.ShowAlways = true;
            tt.SetToolTip(rbConvert, "Convert RS1 Arrangments" +
                Environment.NewLine + "to RS2014 Arrangments");
        }

        private void resetButton_Click(object sender, EventArgs e)
        {
            ((MainForm)ParentForm).ReloadControls();
        }

        private void AddOnChangeHandlerToInputControls()
        {
            // track user changes to form
            DlcNameTB.TextChanged +=
                        new EventHandler(InputControls_OnChange);
            SongDisplayNameSortTB.TextChanged +=
                        new EventHandler(InputControls_OnChange);
            SongDisplayNameTB.TextChanged +=
                        new EventHandler(InputControls_OnChange);
            AlbumTB.TextChanged +=
                        new EventHandler(InputControls_OnChange);
            ArtistTB.TextChanged +=
                        new EventHandler(InputControls_OnChange);
            ArtistSortTB.TextChanged +=
                        new EventHandler(InputControls_OnChange);
            YearTB.TextChanged +=
                        new EventHandler(InputControls_OnChange);
            AverageTempoTB.TextChanged +=
                        new EventHandler(InputControls_OnChange);
            arrangementAddButton.Click +=
                        new EventHandler(InputControls_OnChange);
            arrangementEditButton.Click +=
             new EventHandler(InputControls_OnChange);
            arrangementRemoveButton.Click +=
                         new EventHandler(InputControls_OnChange);
            toneAddButton.Click +=
                         new EventHandler(InputControls_OnChange);
            toneEditButton.Click +=
                         new EventHandler(InputControls_OnChange);
            toneRemoveButton.Click +=
                         new EventHandler(InputControls_OnChange);
            toneDuplicateButton.Click +=
                          new EventHandler(InputControls_OnChange);
            toneImportButton.Click +=
                          new EventHandler(InputControls_OnChange);
        }

        private void InputControls_OnChange(object sender, EventArgs e)
        {
            userChanges++;
        }




    }
}