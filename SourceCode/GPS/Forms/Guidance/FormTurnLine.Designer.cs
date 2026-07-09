namespace AgOpenGPS
{
    partial class FormTurnLine
    {
        private System.ComponentModel.IContainer components = null;

        protected override void Dispose(bool disposing)
        {
            if (disposing && (components != null))
            {
                components.Dispose();
            }
            base.Dispose(disposing);
        }

        #region Windows Form Designer generated code

        private void InitializeComponent()
        {
            this.chkActivate = new System.Windows.Forms.CheckBox();
            this.chkAutomatic = new System.Windows.Forms.CheckBox();
            this.chkAfterStop = new System.Windows.Forms.CheckBox();
            this.lblRadius = new System.Windows.Forms.Label();
            this.btnAddLine = new System.Windows.Forms.Button();
            this.btnAddPoint = new System.Windows.Forms.CheckBox();
            this.btnDeletePoint = new System.Windows.Forms.CheckBox();
            this.btnDeleteLine = new System.Windows.Forms.CheckBox();
            this.chkParcels = new System.Windows.Forms.CheckBox();
            this.btnGoToField = new System.Windows.Forms.Button();
            this.chkKPrimary = new System.Windows.Forms.CheckBox();
            this.chkCombined = new System.Windows.Forms.CheckBox();
            this.btnSave = new System.Windows.Forms.Button();
            this.lblStatus = new System.Windows.Forms.Label();
            this.gMapControl = new GMap.NET.WindowsForms.GMapControl();
            this.btnZoomIn = new System.Windows.Forms.Button();
            this.btnZoomOut = new System.Windows.Forms.Button();
            this.btnWinFull = new System.Windows.Forms.Button();
            this.btnWinClose = new System.Windows.Forms.Button();
            this.btnTabMap = new System.Windows.Forms.Button();
            this.btnTabParams = new System.Windows.Forms.Button();
            this.btnTabPath = new System.Windows.Forms.Button();
            this.pnlParams = new System.Windows.Forms.Panel();
            this.pnlPath = new System.Windows.Forms.Panel();
            this.lblTrack = new System.Windows.Forms.Label();
            this.cboTrack = new System.Windows.Forms.ComboBox();
            this.btnMode1 = new System.Windows.Forms.CheckBox();
            this.btnMode2 = new System.Windows.Forms.CheckBox();
            this.btnMode3 = new System.Windows.Forms.CheckBox();
            this.lblZones = new System.Windows.Forms.Label();
            this.nudZones = new System.Windows.Forms.NumericUpDown();
            this.chkHeadlandClip = new System.Windows.Forms.CheckBox();
            this.lblPlanInfo = new System.Windows.Forms.Label();
            this.btnGeneratePlan = new System.Windows.Forms.Button();
            this.btnAddPath = new System.Windows.Forms.Button();
            this.btnDeletePath = new System.Windows.Forms.Button();
            this.chkFollowPlan = new System.Windows.Forms.CheckBox();
            this.lblPlanStatus = new System.Windows.Forms.Label();
            this.btnStyleK = new System.Windows.Forms.CheckBox();
            this.btnStyleU = new System.Windows.Forms.CheckBox();
            this.btnStyleRS = new System.Windows.Forms.CheckBox();
            this.pnlK = new System.Windows.Forms.Panel();
            this.pnlU = new System.Windows.Forms.Panel();
            this.pnlRS = new System.Windows.Forms.Panel();
            this.lblExitK = new System.Windows.Forms.Label();
            this.lblDistK = new System.Windows.Forms.Label();
            this.lblAngleK = new System.Windows.Forms.Label();
            this.lblTransK = new System.Windows.Forms.Label();
            this.lblOutU = new System.Windows.Forms.Label();
            this.lblInU = new System.Windows.Forms.Label();
            this.lblMinRS = new System.Windows.Forms.Label();
            this.lblHintK = new System.Windows.Forms.Label();
            this.lblHintU = new System.Windows.Forms.Label();
            this.lblHintRS = new System.Windows.Forms.Label();
            this.nudExitK = new System.Windows.Forms.NumericUpDown();
            this.nudDistK = new System.Windows.Forms.NumericUpDown();
            this.nudAngleK = new System.Windows.Forms.NumericUpDown();
            this.nudTransK = new System.Windows.Forms.NumericUpDown();
            this.nudOutU = new System.Windows.Forms.NumericUpDown();
            this.nudInU = new System.Windows.Forms.NumericUpDown();
            this.nudMinRS = new System.Windows.Forms.NumericUpDown();
            this.lblTolerance = new System.Windows.Forms.Label();
            this.nudTolerance = new System.Windows.Forms.NumericUpDown();
            this.lblOvershootU = new System.Windows.Forms.Label();
            this.nudOvershootU = new System.Windows.Forms.NumericUpDown();
            this.SuspendLayout();
            //
            // chkActivate
            //
            this.chkActivate.Appearance = System.Windows.Forms.Appearance.Button;
            this.chkActivate.BackColor = System.Drawing.Color.AliceBlue;
            this.chkActivate.FlatAppearance.CheckedBackColor = System.Drawing.Color.MediumSeaGreen;
            this.chkActivate.FlatStyle = System.Windows.Forms.FlatStyle.Flat;
            this.chkActivate.Font = new System.Drawing.Font("Tahoma", 12F, System.Drawing.FontStyle.Bold);
            this.chkActivate.Location = new System.Drawing.Point(12, 8);
            this.chkActivate.Name = "chkActivate";
            this.chkActivate.Size = new System.Drawing.Size(210, 48);
            this.chkActivate.TabIndex = 0;
            this.chkActivate.Text = "Activate turn";
            this.chkActivate.TextAlign = System.Drawing.ContentAlignment.MiddleCenter;
            this.chkActivate.UseVisualStyleBackColor = false;
            //
            // chkAutomatic
            //
            this.chkAutomatic.Appearance = System.Windows.Forms.Appearance.Button;
            this.chkAutomatic.BackColor = System.Drawing.Color.AliceBlue;
            this.chkAutomatic.FlatAppearance.CheckedBackColor = System.Drawing.Color.MediumAquamarine;
            this.chkAutomatic.FlatStyle = System.Windows.Forms.FlatStyle.Flat;
            this.chkAutomatic.Font = new System.Drawing.Font("Tahoma", 11.25F, System.Drawing.FontStyle.Bold);
            this.chkAutomatic.Location = new System.Drawing.Point(230, 8);
            this.chkAutomatic.Name = "chkAutomatic";
            this.chkAutomatic.Size = new System.Drawing.Size(150, 48);
            this.chkAutomatic.TabIndex = 1;
            this.chkAutomatic.Text = "Automatic turn";
            this.chkAutomatic.TextAlign = System.Drawing.ContentAlignment.MiddleCenter;
            this.chkAutomatic.UseVisualStyleBackColor = false;
            this.chkAutomatic.CheckedChanged += new System.EventHandler(this.chkAutomatic_CheckedChanged);
            //
            // chkAfterStop
            //
            this.chkAfterStop.Appearance = System.Windows.Forms.Appearance.Button;
            this.chkAfterStop.BackColor = System.Drawing.Color.AliceBlue;
            this.chkAfterStop.FlatAppearance.CheckedBackColor = System.Drawing.Color.MediumAquamarine;
            this.chkAfterStop.FlatStyle = System.Windows.Forms.FlatStyle.Flat;
            this.chkAfterStop.Font = new System.Drawing.Font("Tahoma", 11.25F, System.Drawing.FontStyle.Bold);
            this.chkAfterStop.Location = new System.Drawing.Point(648, 8);
            this.chkAfterStop.Name = "chkAfterStop";
            this.chkAfterStop.Size = new System.Drawing.Size(180, 48);
            this.chkAfterStop.TabIndex = 18;
            this.chkAfterStop.Text = "Turn after stop";
            this.chkAfterStop.TextAlign = System.Drawing.ContentAlignment.MiddleCenter;
            this.chkAfterStop.UseVisualStyleBackColor = false;
            this.chkAfterStop.CheckedChanged += new System.EventHandler(this.chkAfterStop_CheckedChanged);
            //
            // lblRadius
            //
            this.lblRadius.Font = new System.Drawing.Font("Tahoma", 11.25F, System.Drawing.FontStyle.Bold);
            this.lblRadius.ForeColor = System.Drawing.Color.DimGray;
            this.lblRadius.Location = new System.Drawing.Point(392, 18);
            this.lblRadius.Name = "lblRadius";
            this.lblRadius.Size = new System.Drawing.Size(250, 30);
            this.lblRadius.TabIndex = 2;
            this.lblRadius.TextAlign = System.Drawing.ContentAlignment.MiddleLeft;
            //
            // btnAddLine
            //
            this.btnAddLine.BackColor = System.Drawing.Color.PapayaWhip;
            this.btnAddLine.FlatStyle = System.Windows.Forms.FlatStyle.Flat;
            this.btnAddLine.Font = new System.Drawing.Font("Tahoma", 11.25F, System.Drawing.FontStyle.Bold);
            this.btnAddLine.Location = new System.Drawing.Point(12, 62);
            this.btnAddLine.Name = "btnAddLine";
            this.btnAddLine.Size = new System.Drawing.Size(150, 44);
            this.btnAddLine.TabIndex = 3;
            this.btnAddLine.Text = "Add line";
            this.btnAddLine.UseVisualStyleBackColor = false;
            this.btnAddLine.Click += new System.EventHandler(this.btnAddLine_Click);
            //
            // btnAddPoint
            //
            this.btnAddPoint.Appearance = System.Windows.Forms.Appearance.Button;
            this.btnAddPoint.BackColor = System.Drawing.Color.AliceBlue;
            this.btnAddPoint.FlatAppearance.CheckedBackColor = System.Drawing.Color.Gold;
            this.btnAddPoint.FlatStyle = System.Windows.Forms.FlatStyle.Flat;
            this.btnAddPoint.Font = new System.Drawing.Font("Tahoma", 10F, System.Drawing.FontStyle.Bold);
            this.btnAddPoint.Location = new System.Drawing.Point(168, 62);
            this.btnAddPoint.Name = "btnAddPoint";
            this.btnAddPoint.Size = new System.Drawing.Size(130, 44);
            this.btnAddPoint.TabIndex = 4;
            this.btnAddPoint.Text = "Add point";
            this.btnAddPoint.TextAlign = System.Drawing.ContentAlignment.MiddleCenter;
            this.btnAddPoint.UseVisualStyleBackColor = false;
            this.btnAddPoint.CheckedChanged += new System.EventHandler(this.btnAddPoint_CheckedChanged);
            //
            // btnDeletePoint
            //
            this.btnDeletePoint.Appearance = System.Windows.Forms.Appearance.Button;
            this.btnDeletePoint.BackColor = System.Drawing.Color.AliceBlue;
            this.btnDeletePoint.FlatAppearance.CheckedBackColor = System.Drawing.Color.IndianRed;
            this.btnDeletePoint.FlatStyle = System.Windows.Forms.FlatStyle.Flat;
            this.btnDeletePoint.Font = new System.Drawing.Font("Tahoma", 10F, System.Drawing.FontStyle.Bold);
            this.btnDeletePoint.Location = new System.Drawing.Point(304, 62);
            this.btnDeletePoint.Name = "btnDeletePoint";
            this.btnDeletePoint.Size = new System.Drawing.Size(130, 44);
            this.btnDeletePoint.TabIndex = 5;
            this.btnDeletePoint.Text = "Delete point";
            this.btnDeletePoint.TextAlign = System.Drawing.ContentAlignment.MiddleCenter;
            this.btnDeletePoint.UseVisualStyleBackColor = false;
            this.btnDeletePoint.CheckedChanged += new System.EventHandler(this.btnDeletePoint_CheckedChanged);
            //
            // btnDeleteLine
            //
            this.btnDeleteLine.Appearance = System.Windows.Forms.Appearance.Button;
            this.btnDeleteLine.BackColor = System.Drawing.Color.AliceBlue;
            this.btnDeleteLine.FlatAppearance.CheckedBackColor = System.Drawing.Color.IndianRed;
            this.btnDeleteLine.FlatStyle = System.Windows.Forms.FlatStyle.Flat;
            this.btnDeleteLine.Font = new System.Drawing.Font("Tahoma", 10F, System.Drawing.FontStyle.Bold);
            this.btnDeleteLine.Location = new System.Drawing.Point(440, 62);
            this.btnDeleteLine.Name = "btnDeleteLine";
            this.btnDeleteLine.Size = new System.Drawing.Size(130, 44);
            this.btnDeleteLine.TabIndex = 6;
            this.btnDeleteLine.Text = "Delete line";
            this.btnDeleteLine.TextAlign = System.Drawing.ContentAlignment.MiddleCenter;
            this.btnDeleteLine.UseVisualStyleBackColor = false;
            this.btnDeleteLine.CheckedChanged += new System.EventHandler(this.btnDeleteLine_CheckedChanged);
            //
            // chkParcels
            //
            this.chkParcels.Appearance = System.Windows.Forms.Appearance.Button;
            this.chkParcels.BackColor = System.Drawing.Color.AliceBlue;
            this.chkParcels.Checked = true;
            this.chkParcels.CheckState = System.Windows.Forms.CheckState.Checked;
            this.chkParcels.FlatAppearance.CheckedBackColor = System.Drawing.Color.MediumAquamarine;
            this.chkParcels.FlatStyle = System.Windows.Forms.FlatStyle.Flat;
            this.chkParcels.Font = new System.Drawing.Font("Tahoma", 10F, System.Drawing.FontStyle.Bold);
            this.chkParcels.Location = new System.Drawing.Point(576, 62);
            this.chkParcels.Name = "chkParcels";
            this.chkParcels.Size = new System.Drawing.Size(160, 44);
            this.chkParcels.TabIndex = 7;
            this.chkParcels.Text = "Cadastral parcels";
            this.chkParcels.TextAlign = System.Drawing.ContentAlignment.MiddleCenter;
            this.chkParcels.UseVisualStyleBackColor = false;
            this.chkParcels.CheckedChanged += new System.EventHandler(this.chkParcels_CheckedChanged);
            //
            // btnGoToField
            //
            this.btnGoToField.Font = new System.Drawing.Font("Tahoma", 10F, System.Drawing.FontStyle.Bold);
            this.btnGoToField.Location = new System.Drawing.Point(742, 62);
            this.btnGoToField.Name = "btnGoToField";
            this.btnGoToField.Size = new System.Drawing.Size(140, 44);
            this.btnGoToField.TabIndex = 8;
            this.btnGoToField.Text = "Go to position";
            this.btnGoToField.UseVisualStyleBackColor = true;
            this.btnGoToField.Click += new System.EventHandler(this.btnGoToField_Click);
            //
            // chkKPrimary
            //
            this.chkKPrimary.Appearance = System.Windows.Forms.Appearance.Button;
            this.chkKPrimary.BackColor = System.Drawing.Color.AliceBlue;
            this.chkKPrimary.FlatAppearance.CheckedBackColor = System.Drawing.Color.Gold;
            this.chkKPrimary.FlatStyle = System.Windows.Forms.FlatStyle.Flat;
            this.chkKPrimary.Font = new System.Drawing.Font("Tahoma", 11.25F, System.Drawing.FontStyle.Bold);
            this.chkKPrimary.Location = new System.Drawing.Point(12, 112);
            this.chkKPrimary.Name = "chkKPrimary";
            this.chkKPrimary.Size = new System.Drawing.Size(190, 44);
            this.chkKPrimary.TabIndex = 9;
            this.chkKPrimary.Text = "K-turn primary";
            this.chkKPrimary.TextAlign = System.Drawing.ContentAlignment.MiddleCenter;
            this.chkKPrimary.UseVisualStyleBackColor = false;
            this.chkKPrimary.CheckedChanged += new System.EventHandler(this.chkKPrimary_CheckedChanged);
            //
            // chkCombined
            //
            this.chkCombined.Appearance = System.Windows.Forms.Appearance.Button;
            this.chkCombined.BackColor = System.Drawing.Color.AliceBlue;
            this.chkCombined.FlatAppearance.CheckedBackColor = System.Drawing.Color.Gold;
            this.chkCombined.FlatStyle = System.Windows.Forms.FlatStyle.Flat;
            this.chkCombined.Font = new System.Drawing.Font("Tahoma", 11.25F, System.Drawing.FontStyle.Bold);
            this.chkCombined.Location = new System.Drawing.Point(208, 112);
            this.chkCombined.Name = "chkCombined";
            this.chkCombined.Size = new System.Drawing.Size(190, 44);
            this.chkCombined.TabIndex = 10;
            this.chkCombined.Text = "Combined U/K";
            this.chkCombined.TextAlign = System.Drawing.ContentAlignment.MiddleCenter;
            this.chkCombined.UseVisualStyleBackColor = false;
            this.chkCombined.CheckedChanged += new System.EventHandler(this.chkCombined_CheckedChanged);
            //
            // btnSave
            //
            this.btnSave.BackColor = System.Drawing.Color.Honeydew;
            this.btnSave.FlatStyle = System.Windows.Forms.FlatStyle.Flat;
            this.btnSave.Font = new System.Drawing.Font("Tahoma", 12F, System.Drawing.FontStyle.Bold);
            this.btnSave.Location = new System.Drawing.Point(412, 112);
            this.btnSave.Name = "btnSave";
            this.btnSave.Size = new System.Drawing.Size(160, 44);
            this.btnSave.TabIndex = 11;
            this.btnSave.Text = "OK";
            this.btnSave.UseVisualStyleBackColor = false;
            this.btnSave.Click += new System.EventHandler(this.btnSave_Click);
            //
            // lblStatus
            //
            this.lblStatus.Anchor = ((System.Windows.Forms.AnchorStyles)((System.Windows.Forms.AnchorStyles.Top | System.Windows.Forms.AnchorStyles.Left)));
            this.lblStatus.Font = new System.Drawing.Font("Tahoma", 9.75F, System.Drawing.FontStyle.Bold);
            this.lblStatus.ForeColor = System.Drawing.Color.DimGray;
            this.lblStatus.Location = new System.Drawing.Point(586, 112);
            this.lblStatus.Name = "lblStatus";
            this.lblStatus.Size = new System.Drawing.Size(290, 48);
            this.lblStatus.TabIndex = 12;
            this.lblStatus.TextAlign = System.Drawing.ContentAlignment.MiddleLeft;
            //
            // gMapControl
            //
            this.gMapControl.Anchor = ((System.Windows.Forms.AnchorStyles)((((System.Windows.Forms.AnchorStyles.Top | System.Windows.Forms.AnchorStyles.Bottom)
                        | System.Windows.Forms.AnchorStyles.Left)
                        | System.Windows.Forms.AnchorStyles.Right)));
            this.gMapControl.Bearing = 0F;
            this.gMapControl.CanDragMap = true;
            this.gMapControl.GrayScaleMode = false;
            this.gMapControl.Location = new System.Drawing.Point(12, 164);
            this.gMapControl.MarkersEnabled = true;
            this.gMapControl.MaxZoom = 22;
            this.gMapControl.MinZoom = 2;
            this.gMapControl.MouseWheelZoomType = GMap.NET.MouseWheelZoomType.MousePositionAndCenter;
            this.gMapControl.Name = "gMapControl";
            this.gMapControl.NegativeMode = false;
            this.gMapControl.PolygonsEnabled = true;
            this.gMapControl.RoutesEnabled = true;
            this.gMapControl.ShowTileGridLines = false;
            this.gMapControl.Size = new System.Drawing.Size(1016, 508);
            this.gMapControl.TabIndex = 13;
            this.gMapControl.Zoom = 0D;
            //
            // btnZoomIn
            //
            this.btnZoomIn.Anchor = ((System.Windows.Forms.AnchorStyles)((System.Windows.Forms.AnchorStyles.Top | System.Windows.Forms.AnchorStyles.Right)));
            this.btnZoomIn.BackColor = System.Drawing.Color.White;
            this.btnZoomIn.Font = new System.Drawing.Font("Tahoma", 20F, System.Drawing.FontStyle.Bold);
            this.btnZoomIn.Location = new System.Drawing.Point(960, 174);
            this.btnZoomIn.Name = "btnZoomIn";
            this.btnZoomIn.Size = new System.Drawing.Size(56, 56);
            this.btnZoomIn.TabIndex = 14;
            this.btnZoomIn.Text = "+";
            this.btnZoomIn.UseVisualStyleBackColor = false;
            this.btnZoomIn.Click += new System.EventHandler(this.btnZoomIn_Click);
            //
            // btnZoomOut
            //
            this.btnZoomOut.Anchor = ((System.Windows.Forms.AnchorStyles)((System.Windows.Forms.AnchorStyles.Top | System.Windows.Forms.AnchorStyles.Right)));
            this.btnZoomOut.BackColor = System.Drawing.Color.White;
            this.btnZoomOut.Font = new System.Drawing.Font("Tahoma", 20F, System.Drawing.FontStyle.Bold);
            this.btnZoomOut.Location = new System.Drawing.Point(960, 236);
            this.btnZoomOut.Name = "btnZoomOut";
            this.btnZoomOut.Size = new System.Drawing.Size(56, 56);
            this.btnZoomOut.TabIndex = 15;
            this.btnZoomOut.Text = "−";
            this.btnZoomOut.UseVisualStyleBackColor = false;
            this.btnZoomOut.Click += new System.EventHandler(this.btnZoomOut_Click);
            //
            // btnWinFull
            //
            this.btnWinFull.Anchor = ((System.Windows.Forms.AnchorStyles)((System.Windows.Forms.AnchorStyles.Top | System.Windows.Forms.AnchorStyles.Right)));
            this.btnWinFull.BackColor = System.Drawing.Color.Transparent;
            this.btnWinFull.BackgroundImage = global::AgOpenGPS.Properties.Resources.WindowMaximize;
            this.btnWinFull.BackgroundImageLayout = System.Windows.Forms.ImageLayout.Zoom;
            this.btnWinFull.FlatAppearance.BorderSize = 0;
            this.btnWinFull.FlatStyle = System.Windows.Forms.FlatStyle.Flat;
            this.btnWinFull.Location = new System.Drawing.Point(910, 4);
            this.btnWinFull.Name = "btnWinFull";
            this.btnWinFull.Size = new System.Drawing.Size(56, 40);
            this.btnWinFull.TabIndex = 16;
            this.btnWinFull.UseVisualStyleBackColor = false;
            this.btnWinFull.Click += new System.EventHandler(this.btnWinFull_Click);
            //
            // btnWinClose
            //
            this.btnWinClose.Anchor = ((System.Windows.Forms.AnchorStyles)((System.Windows.Forms.AnchorStyles.Top | System.Windows.Forms.AnchorStyles.Right)));
            this.btnWinClose.BackColor = System.Drawing.Color.Transparent;
            this.btnWinClose.BackgroundImage = global::AgOpenGPS.Properties.Resources.WindowClose;
            this.btnWinClose.BackgroundImageLayout = System.Windows.Forms.ImageLayout.Zoom;
            this.btnWinClose.FlatAppearance.BorderSize = 0;
            this.btnWinClose.FlatStyle = System.Windows.Forms.FlatStyle.Flat;
            this.btnWinClose.Location = new System.Drawing.Point(972, 4);
            this.btnWinClose.Name = "btnWinClose";
            this.btnWinClose.Size = new System.Drawing.Size(56, 40);
            this.btnWinClose.TabIndex = 17;
            this.btnWinClose.UseVisualStyleBackColor = false;
            this.btnWinClose.Click += new System.EventHandler(this.btnWinClose_Click);
            ((System.ComponentModel.ISupportInitialize)(this.nudExitK)).BeginInit();
            ((System.ComponentModel.ISupportInitialize)(this.nudDistK)).BeginInit();
            ((System.ComponentModel.ISupportInitialize)(this.nudAngleK)).BeginInit();
            ((System.ComponentModel.ISupportInitialize)(this.nudTransK)).BeginInit();
            ((System.ComponentModel.ISupportInitialize)(this.nudOutU)).BeginInit();
            ((System.ComponentModel.ISupportInitialize)(this.nudInU)).BeginInit();
            ((System.ComponentModel.ISupportInitialize)(this.nudMinRS)).BeginInit();
            ((System.ComponentModel.ISupportInitialize)(this.nudTolerance)).BeginInit();
            ((System.ComponentModel.ISupportInitialize)(this.nudOvershootU)).BeginInit();
            ((System.ComponentModel.ISupportInitialize)(this.nudZones)).BeginInit();
            //
            // btnTabMap / btnTabParams / btnTabPath (icon tab buttons)
            //
            this.btnTabMap.Anchor = ((System.Windows.Forms.AnchorStyles)((System.Windows.Forms.AnchorStyles.Top | System.Windows.Forms.AnchorStyles.Right)));
            this.btnTabMap.BackColor = System.Drawing.Color.Gold;
            this.btnTabMap.BackgroundImage = global::AgOpenGPS.Properties.Resources.MappingOn;
            this.btnTabMap.BackgroundImageLayout = System.Windows.Forms.ImageLayout.Zoom;
            this.btnTabMap.FlatStyle = System.Windows.Forms.FlatStyle.Flat;
            this.btnTabMap.Location = new System.Drawing.Point(886, 112);
            this.btnTabMap.Name = "btnTabMap";
            this.btnTabMap.Size = new System.Drawing.Size(46, 44);
            this.btnTabMap.TabIndex = 19;
            this.btnTabMap.UseVisualStyleBackColor = false;
            this.btnTabMap.Click += new System.EventHandler(this.btnTabMap_Click);
            this.btnTabParams.Anchor = ((System.Windows.Forms.AnchorStyles)((System.Windows.Forms.AnchorStyles.Top | System.Windows.Forms.AnchorStyles.Right)));
            this.btnTabParams.BackColor = System.Drawing.Color.Lavender;
            this.btnTabParams.BackgroundImage = global::AgOpenGPS.Properties.Resources.Settings48;
            this.btnTabParams.BackgroundImageLayout = System.Windows.Forms.ImageLayout.Zoom;
            this.btnTabParams.FlatStyle = System.Windows.Forms.FlatStyle.Flat;
            this.btnTabParams.Location = new System.Drawing.Point(934, 112);
            this.btnTabParams.Name = "btnTabParams";
            this.btnTabParams.Size = new System.Drawing.Size(46, 44);
            this.btnTabParams.TabIndex = 20;
            this.btnTabParams.UseVisualStyleBackColor = false;
            this.btnTabParams.Click += new System.EventHandler(this.btnTabParams_Click);
            this.btnTabPath.Anchor = ((System.Windows.Forms.AnchorStyles)((System.Windows.Forms.AnchorStyles.Top | System.Windows.Forms.AnchorStyles.Right)));
            this.btnTabPath.BackColor = System.Drawing.Color.Lavender;
            this.btnTabPath.BackgroundImage = global::AgOpenGPS.Properties.Resources.ABTracks;
            this.btnTabPath.BackgroundImageLayout = System.Windows.Forms.ImageLayout.Zoom;
            this.btnTabPath.FlatStyle = System.Windows.Forms.FlatStyle.Flat;
            this.btnTabPath.Location = new System.Drawing.Point(982, 112);
            this.btnTabPath.Name = "btnTabPath";
            this.btnTabPath.Size = new System.Drawing.Size(46, 44);
            this.btnTabPath.TabIndex = 21;
            this.btnTabPath.UseVisualStyleBackColor = false;
            this.btnTabPath.Click += new System.EventHandler(this.btnTabPath_Click);
            //
            // pnlParams
            //
            this.pnlParams.Anchor = ((System.Windows.Forms.AnchorStyles)((((System.Windows.Forms.AnchorStyles.Top | System.Windows.Forms.AnchorStyles.Bottom)
                        | System.Windows.Forms.AnchorStyles.Left)
                        | System.Windows.Forms.AnchorStyles.Right)));
            this.pnlParams.BackColor = System.Drawing.Color.WhiteSmoke;
            this.pnlParams.BorderStyle = System.Windows.Forms.BorderStyle.FixedSingle;
            this.pnlParams.Location = new System.Drawing.Point(12, 164);
            this.pnlParams.Name = "pnlParams";
            this.pnlParams.Size = new System.Drawing.Size(1016, 508);
            this.pnlParams.TabIndex = 20;
            this.pnlParams.Visible = false;
            this.pnlParams.Controls.Add(this.btnStyleK);
            this.pnlParams.Controls.Add(this.btnStyleU);
            this.pnlParams.Controls.Add(this.btnStyleRS);
            this.pnlParams.Controls.Add(this.lblTolerance);
            this.pnlParams.Controls.Add(this.nudTolerance);
            this.pnlParams.Controls.Add(this.pnlK);
            this.pnlParams.Controls.Add(this.pnlU);
            this.pnlParams.Controls.Add(this.pnlRS);
            //
            // pnlPath (left strip over the map; the plan draws on the map itself)
            //
            this.pnlPath.Anchor = ((System.Windows.Forms.AnchorStyles)(((System.Windows.Forms.AnchorStyles.Top | System.Windows.Forms.AnchorStyles.Bottom)
                        | System.Windows.Forms.AnchorStyles.Left)));
            this.pnlPath.BackColor = System.Drawing.Color.WhiteSmoke;
            this.pnlPath.BorderStyle = System.Windows.Forms.BorderStyle.FixedSingle;
            this.pnlPath.Location = new System.Drawing.Point(12, 164);
            this.pnlPath.Name = "pnlPath";
            this.pnlPath.Size = new System.Drawing.Size(310, 508);
            this.pnlPath.TabIndex = 22;
            this.pnlPath.Visible = false;
            this.pnlPath.Controls.Add(this.lblTrack);
            this.pnlPath.Controls.Add(this.cboTrack);
            this.pnlPath.Controls.Add(this.btnMode1);
            this.pnlPath.Controls.Add(this.btnMode3);
            this.pnlPath.Controls.Add(this.btnMode2);
            this.pnlPath.Controls.Add(this.lblZones);
            this.pnlPath.Controls.Add(this.nudZones);
            this.pnlPath.Controls.Add(this.chkHeadlandClip);
            this.pnlPath.Controls.Add(this.lblPlanInfo);
            this.pnlPath.Controls.Add(this.btnGeneratePlan);
            this.pnlPath.Controls.Add(this.btnAddPath);
            this.pnlPath.Controls.Add(this.btnDeletePath);
            this.pnlPath.Controls.Add(this.chkFollowPlan);
            this.pnlPath.Controls.Add(this.lblPlanStatus);
            //
            // lblTrack + cboTrack (which line the plan is generated from)
            //
            this.lblTrack.Font = new System.Drawing.Font("Tahoma", 11.25F, System.Drawing.FontStyle.Bold);
            this.lblTrack.Location = new System.Drawing.Point(10, 58);
            this.lblTrack.Name = "lblTrack";
            this.lblTrack.Size = new System.Drawing.Size(64, 30);
            this.lblTrack.TabIndex = 9;
            this.lblTrack.Text = "Track";
            this.lblTrack.TextAlign = System.Drawing.ContentAlignment.MiddleLeft;
            this.cboTrack.DropDownStyle = System.Windows.Forms.ComboBoxStyle.DropDownList;
            this.cboTrack.Font = new System.Drawing.Font("Tahoma", 11.25F);
            this.cboTrack.Location = new System.Drawing.Point(80, 56);
            this.cboTrack.Name = "cboTrack";
            this.cboTrack.Size = new System.Drawing.Size(218, 34);
            this.cboTrack.TabIndex = 10;
            //
            // btnMode1 / btnMode3 / btnMode2 (processing mode)
            //
            this.btnMode1.Appearance = System.Windows.Forms.Appearance.Button;
            this.btnMode1.BackColor = System.Drawing.Color.AliceBlue;
            this.btnMode1.FlatAppearance.CheckedBackColor = System.Drawing.Color.Gold;
            this.btnMode1.FlatStyle = System.Windows.Forms.FlatStyle.Flat;
            this.btnMode1.Font = new System.Drawing.Font("Tahoma", 11.25F, System.Drawing.FontStyle.Bold);
            this.btnMode1.Location = new System.Drawing.Point(10, 98);
            this.btnMode1.Name = "btnMode1";
            this.btnMode1.Size = new System.Drawing.Size(288, 38);
            this.btnMode1.TabIndex = 0;
            this.btnMode1.Text = "Row by row";
            this.btnMode1.TextAlign = System.Drawing.ContentAlignment.MiddleCenter;
            this.btnMode1.UseVisualStyleBackColor = false;
            this.btnMode1.CheckedChanged += new System.EventHandler(this.btnMode1_CheckedChanged);
            this.btnMode3.Appearance = System.Windows.Forms.Appearance.Button;
            this.btnMode3.BackColor = System.Drawing.Color.AliceBlue;
            this.btnMode3.Checked = true;
            this.btnMode3.CheckState = System.Windows.Forms.CheckState.Checked;
            this.btnMode3.FlatAppearance.CheckedBackColor = System.Drawing.Color.Gold;
            this.btnMode3.FlatStyle = System.Windows.Forms.FlatStyle.Flat;
            this.btnMode3.Font = new System.Drawing.Font("Tahoma", 11.25F, System.Drawing.FontStyle.Bold);
            this.btnMode3.Location = new System.Drawing.Point(10, 140);
            this.btnMode3.Name = "btnMode3";
            this.btnMode3.Size = new System.Drawing.Size(288, 38);
            this.btnMode3.TabIndex = 1;
            this.btnMode3.Text = "Zones (skip)";
            this.btnMode3.TextAlign = System.Drawing.ContentAlignment.MiddleCenter;
            this.btnMode3.UseVisualStyleBackColor = false;
            this.btnMode3.CheckedChanged += new System.EventHandler(this.btnMode3_CheckedChanged);
            this.btnMode2.Appearance = System.Windows.Forms.Appearance.Button;
            this.btnMode2.BackColor = System.Drawing.Color.AliceBlue;
            this.btnMode2.FlatAppearance.CheckedBackColor = System.Drawing.Color.Gold;
            this.btnMode2.FlatStyle = System.Windows.Forms.FlatStyle.Flat;
            this.btnMode2.Font = new System.Drawing.Font("Tahoma", 11.25F, System.Drawing.FontStyle.Bold);
            this.btnMode2.Location = new System.Drawing.Point(10, 182);
            this.btnMode2.Name = "btnMode2";
            this.btnMode2.Size = new System.Drawing.Size(288, 38);
            this.btnMode2.TabIndex = 2;
            this.btnMode2.Text = "Contour";
            this.btnMode2.TextAlign = System.Drawing.ContentAlignment.MiddleCenter;
            this.btnMode2.UseVisualStyleBackColor = false;
            this.btnMode2.CheckedChanged += new System.EventHandler(this.btnMode2_CheckedChanged);
            //
            // lblZones + nudZones
            //
            this.lblZones.Font = new System.Drawing.Font("Tahoma", 11.25F, System.Drawing.FontStyle.Bold);
            this.lblZones.Location = new System.Drawing.Point(10, 230);
            this.lblZones.Name = "lblZones";
            this.lblZones.Size = new System.Drawing.Size(104, 30);
            this.lblZones.TabIndex = 3;
            this.lblZones.Text = "Zones";
            this.lblZones.TextAlign = System.Drawing.ContentAlignment.MiddleLeft;
            this.nudZones.Font = new System.Drawing.Font("Tahoma", 12F);
            this.nudZones.Location = new System.Drawing.Point(120, 226);
            this.nudZones.Maximum = new decimal(new int[] { 9, 0, 0, 0 });
            this.nudZones.Minimum = new decimal(new int[] { 1, 0, 0, 0 });
            this.nudZones.Name = "nudZones";
            this.nudZones.Size = new System.Drawing.Size(84, 34);
            this.nudZones.TabIndex = 4;
            this.nudZones.Value = new decimal(new int[] { 2, 0, 0, 0 });
            this.nudZones.ValueChanged += new System.EventHandler(this.nudZones_ValueChanged);
            //
            // chkHeadlandClip (generate passes only inside the headland line)
            //
            this.chkHeadlandClip.Appearance = System.Windows.Forms.Appearance.Button;
            this.chkHeadlandClip.BackColor = System.Drawing.Color.AliceBlue;
            this.chkHeadlandClip.Checked = true;
            this.chkHeadlandClip.CheckState = System.Windows.Forms.CheckState.Checked;
            this.chkHeadlandClip.FlatAppearance.CheckedBackColor = System.Drawing.Color.MediumAquamarine;
            this.chkHeadlandClip.FlatStyle = System.Windows.Forms.FlatStyle.Flat;
            this.chkHeadlandClip.Font = new System.Drawing.Font("Tahoma", 10F, System.Drawing.FontStyle.Bold);
            this.chkHeadlandClip.Location = new System.Drawing.Point(10, 268);
            this.chkHeadlandClip.Name = "chkHeadlandClip";
            this.chkHeadlandClip.Size = new System.Drawing.Size(288, 38);
            this.chkHeadlandClip.TabIndex = 11;
            this.chkHeadlandClip.Text = "Inside headland";
            this.chkHeadlandClip.TextAlign = System.Drawing.ContentAlignment.MiddleCenter;
            this.chkHeadlandClip.UseVisualStyleBackColor = false;
            //
            // btnAddPath (one manual closing pass beside the selected line)
            //
            this.btnAddPath.BackColor = System.Drawing.Color.PapayaWhip;
            this.btnAddPath.FlatStyle = System.Windows.Forms.FlatStyle.Flat;
            this.btnAddPath.Font = new System.Drawing.Font("Tahoma", 11.25F, System.Drawing.FontStyle.Bold);
            this.btnAddPath.Location = new System.Drawing.Point(10, 398);
            this.btnAddPath.Name = "btnAddPath";
            this.btnAddPath.Size = new System.Drawing.Size(140, 38);
            this.btnAddPath.TabIndex = 12;
            this.btnAddPath.Text = "Add path";
            this.btnAddPath.UseVisualStyleBackColor = false;
            this.btnAddPath.Click += new System.EventHandler(this.btnAddPath_Click);
            //
            // btnDeletePath (undo the last manually added pass)
            //
            this.btnDeletePath.BackColor = System.Drawing.Color.MistyRose;
            this.btnDeletePath.FlatStyle = System.Windows.Forms.FlatStyle.Flat;
            this.btnDeletePath.Font = new System.Drawing.Font("Tahoma", 11.25F, System.Drawing.FontStyle.Bold);
            this.btnDeletePath.Location = new System.Drawing.Point(158, 398);
            this.btnDeletePath.Name = "btnDeletePath";
            this.btnDeletePath.Size = new System.Drawing.Size(140, 38);
            this.btnDeletePath.TabIndex = 13;
            this.btnDeletePath.Text = "Delete path";
            this.btnDeletePath.UseVisualStyleBackColor = false;
            this.btnDeletePath.Click += new System.EventHandler(this.btnDeletePath_Click);
            //
            // lblPlanInfo
            //
            this.lblPlanInfo.Font = new System.Drawing.Font("Tahoma", 9.75F);
            this.lblPlanInfo.ForeColor = System.Drawing.Color.DimGray;
            this.lblPlanInfo.Location = new System.Drawing.Point(10, 312);
            this.lblPlanInfo.Name = "lblPlanInfo";
            this.lblPlanInfo.Size = new System.Drawing.Size(288, 44);
            this.lblPlanInfo.TabIndex = 5;
            //
            // btnGeneratePlan
            //
            this.btnGeneratePlan.BackColor = System.Drawing.Color.PapayaWhip;
            this.btnGeneratePlan.FlatStyle = System.Windows.Forms.FlatStyle.Flat;
            this.btnGeneratePlan.Font = new System.Drawing.Font("Tahoma", 11.25F, System.Drawing.FontStyle.Bold);
            this.btnGeneratePlan.Location = new System.Drawing.Point(10, 358);
            this.btnGeneratePlan.Name = "btnGeneratePlan";
            this.btnGeneratePlan.Size = new System.Drawing.Size(288, 38);
            this.btnGeneratePlan.TabIndex = 6;
            this.btnGeneratePlan.Text = "Generate plan";
            this.btnGeneratePlan.UseVisualStyleBackColor = false;
            this.btnGeneratePlan.Click += new System.EventHandler(this.btnGeneratePlan_Click);
            //
            // chkFollowPlan
            //
            this.chkFollowPlan.Appearance = System.Windows.Forms.Appearance.Button;
            this.chkFollowPlan.BackColor = System.Drawing.Color.AliceBlue;
            this.chkFollowPlan.FlatAppearance.CheckedBackColor = System.Drawing.Color.MediumSeaGreen;
            this.chkFollowPlan.FlatStyle = System.Windows.Forms.FlatStyle.Flat;
            this.chkFollowPlan.Font = new System.Drawing.Font("Tahoma", 12F, System.Drawing.FontStyle.Bold);
            this.chkFollowPlan.Location = new System.Drawing.Point(10, 12);
            this.chkFollowPlan.Name = "chkFollowPlan";
            this.chkFollowPlan.Size = new System.Drawing.Size(288, 38);
            this.chkFollowPlan.TabIndex = 7;
            this.chkFollowPlan.Text = "Follow plan";
            this.chkFollowPlan.TextAlign = System.Drawing.ContentAlignment.MiddleCenter;
            this.chkFollowPlan.UseVisualStyleBackColor = false;
            //
            // lblPlanStatus
            //
            this.lblPlanStatus.Font = new System.Drawing.Font("Tahoma", 9.75F, System.Drawing.FontStyle.Bold);
            this.lblPlanStatus.ForeColor = System.Drawing.Color.DimGray;
            this.lblPlanStatus.Location = new System.Drawing.Point(10, 442);
            this.lblPlanStatus.Name = "lblPlanStatus";
            this.lblPlanStatus.Size = new System.Drawing.Size(288, 28);
            this.lblPlanStatus.TabIndex = 8;
            //
            // btnStyleK
            //
            this.btnStyleK.Appearance = System.Windows.Forms.Appearance.Button;
            this.btnStyleK.BackColor = System.Drawing.Color.AliceBlue;
            this.btnStyleK.FlatAppearance.CheckedBackColor = System.Drawing.Color.Gold;
            this.btnStyleK.FlatStyle = System.Windows.Forms.FlatStyle.Flat;
            this.btnStyleK.Font = new System.Drawing.Font("Tahoma", 12F, System.Drawing.FontStyle.Bold);
            this.btnStyleK.Location = new System.Drawing.Point(16, 12);
            this.btnStyleK.Name = "btnStyleK";
            this.btnStyleK.Size = new System.Drawing.Size(150, 46);
            this.btnStyleK.TabIndex = 0;
            this.btnStyleK.Text = "K-turn";
            this.btnStyleK.TextAlign = System.Drawing.ContentAlignment.MiddleCenter;
            this.btnStyleK.UseVisualStyleBackColor = false;
            this.btnStyleK.CheckedChanged += new System.EventHandler(this.btnStyleK_CheckedChanged);
            //
            // btnStyleU
            //
            this.btnStyleU.Appearance = System.Windows.Forms.Appearance.Button;
            this.btnStyleU.BackColor = System.Drawing.Color.AliceBlue;
            this.btnStyleU.FlatAppearance.CheckedBackColor = System.Drawing.Color.Gold;
            this.btnStyleU.FlatStyle = System.Windows.Forms.FlatStyle.Flat;
            this.btnStyleU.Font = new System.Drawing.Font("Tahoma", 12F, System.Drawing.FontStyle.Bold);
            this.btnStyleU.Location = new System.Drawing.Point(176, 12);
            this.btnStyleU.Name = "btnStyleU";
            this.btnStyleU.Size = new System.Drawing.Size(150, 46);
            this.btnStyleU.TabIndex = 1;
            this.btnStyleU.Text = "U-turn";
            this.btnStyleU.TextAlign = System.Drawing.ContentAlignment.MiddleCenter;
            this.btnStyleU.UseVisualStyleBackColor = false;
            this.btnStyleU.CheckedChanged += new System.EventHandler(this.btnStyleU_CheckedChanged);
            //
            // btnStyleRS
            //
            this.btnStyleRS.Appearance = System.Windows.Forms.Appearance.Button;
            this.btnStyleRS.BackColor = System.Drawing.Color.AliceBlue;
            this.btnStyleRS.FlatAppearance.CheckedBackColor = System.Drawing.Color.Gold;
            this.btnStyleRS.FlatStyle = System.Windows.Forms.FlatStyle.Flat;
            this.btnStyleRS.Font = new System.Drawing.Font("Tahoma", 12F, System.Drawing.FontStyle.Bold);
            this.btnStyleRS.Location = new System.Drawing.Point(336, 12);
            this.btnStyleRS.Name = "btnStyleRS";
            this.btnStyleRS.Size = new System.Drawing.Size(150, 46);
            this.btnStyleRS.TabIndex = 2;
            this.btnStyleRS.Text = "RS (soon)";
            this.btnStyleRS.TextAlign = System.Drawing.ContentAlignment.MiddleCenter;
            this.btnStyleRS.UseVisualStyleBackColor = false;
            this.btnStyleRS.CheckedChanged += new System.EventHandler(this.btnStyleRS_CheckedChanged);
            //
            // pnlK
            //
            this.pnlK.Location = new System.Drawing.Point(16, 72);
            this.pnlK.Name = "pnlK";
            this.pnlK.Size = new System.Drawing.Size(984, 420);
            this.pnlK.TabIndex = 3;
            this.pnlK.Controls.Add(this.lblExitK);
            this.pnlK.Controls.Add(this.nudExitK);
            this.pnlK.Controls.Add(this.lblDistK);
            this.pnlK.Controls.Add(this.nudDistK);
            this.pnlK.Controls.Add(this.lblAngleK);
            this.pnlK.Controls.Add(this.nudAngleK);
            this.pnlK.Controls.Add(this.lblTransK);
            this.pnlK.Controls.Add(this.nudTransK);
            this.pnlK.Controls.Add(this.lblHintK);
            //
            // pnlU
            //
            this.pnlU.Location = new System.Drawing.Point(16, 72);
            this.pnlU.Name = "pnlU";
            this.pnlU.Size = new System.Drawing.Size(984, 420);
            this.pnlU.TabIndex = 4;
            this.pnlU.Visible = false;
            this.pnlU.Controls.Add(this.lblOutU);
            this.pnlU.Controls.Add(this.nudOutU);
            this.pnlU.Controls.Add(this.lblInU);
            this.pnlU.Controls.Add(this.nudInU);
            this.pnlU.Controls.Add(this.lblOvershootU);
            this.pnlU.Controls.Add(this.nudOvershootU);
            this.pnlU.Controls.Add(this.lblHintU);
            //
            // pnlRS
            //
            this.pnlRS.Location = new System.Drawing.Point(16, 72);
            this.pnlRS.Name = "pnlRS";
            this.pnlRS.Size = new System.Drawing.Size(984, 420);
            this.pnlRS.TabIndex = 5;
            this.pnlRS.Visible = false;
            this.pnlRS.Controls.Add(this.lblMinRS);
            this.pnlRS.Controls.Add(this.nudMinRS);
            this.pnlRS.Controls.Add(this.lblHintRS);
            //
            // K labels + inputs
            //
            this.lblExitK.Font = new System.Drawing.Font("Tahoma", 11.25F, System.Drawing.FontStyle.Bold);
            this.lblExitK.Location = new System.Drawing.Point(4, 24);
            this.lblExitK.Name = "lblExitK";
            this.lblExitK.Size = new System.Drawing.Size(320, 30);
            this.lblExitK.TabIndex = 0;
            this.lblExitK.Text = "Exit radius";
            this.lblExitK.TextAlign = System.Drawing.ContentAlignment.MiddleLeft;
            this.lblDistK.Font = new System.Drawing.Font("Tahoma", 11.25F, System.Drawing.FontStyle.Bold);
            this.lblDistK.Location = new System.Drawing.Point(4, 74);
            this.lblDistK.Name = "lblDistK";
            this.lblDistK.Size = new System.Drawing.Size(320, 30);
            this.lblDistK.TabIndex = 1;
            this.lblDistK.Text = "Entry start (m before line)";
            this.lblDistK.TextAlign = System.Drawing.ContentAlignment.MiddleLeft;
            this.lblAngleK.Font = new System.Drawing.Font("Tahoma", 11.25F, System.Drawing.FontStyle.Bold);
            this.lblAngleK.Location = new System.Drawing.Point(4, 124);
            this.lblAngleK.Name = "lblAngleK";
            this.lblAngleK.Size = new System.Drawing.Size(320, 30);
            this.lblAngleK.TabIndex = 2;
            this.lblAngleK.Text = "Entry angle (deg)";
            this.lblAngleK.TextAlign = System.Drawing.ContentAlignment.MiddleLeft;
            this.lblTransK.Font = new System.Drawing.Font("Tahoma", 11.25F, System.Drawing.FontStyle.Bold);
            this.lblTransK.Location = new System.Drawing.Point(4, 174);
            this.lblTransK.Name = "lblTransK";
            this.lblTransK.Size = new System.Drawing.Size(320, 30);
            this.lblTransK.TabIndex = 3;
            this.lblTransK.Text = "Transition radius";
            this.lblTransK.TextAlign = System.Drawing.ContentAlignment.MiddleLeft;
            this.lblHintK.Font = new System.Drawing.Font("Tahoma", 9.75F, System.Drawing.FontStyle.Italic);
            this.lblHintK.ForeColor = System.Drawing.Color.DimGray;
            this.lblHintK.Location = new System.Drawing.Point(4, 236);
            this.lblHintK.Name = "lblHintK";
            this.lblHintK.Size = new System.Drawing.Size(452, 150);
            this.lblHintK.TabIndex = 4;
            this.lblHintK.Text = "Radius 0 = use general turn radius. Exit radius / entry start / angle shape the K-turn.";
            this.nudExitK.DecimalPlaces = 1;
            this.nudExitK.Font = new System.Drawing.Font("Tahoma", 12F);
            this.nudExitK.Increment = new decimal(new int[] { 1, 0, 0, 65536 });
            this.nudExitK.Location = new System.Drawing.Point(330, 20);
            this.nudExitK.Maximum = new decimal(new int[] { 60, 0, 0, 0 });
            this.nudExitK.Name = "nudExitK";
            this.nudExitK.Size = new System.Drawing.Size(130, 34);
            this.nudExitK.TabIndex = 5;
            this.nudDistK.DecimalPlaces = 1;
            this.nudDistK.Font = new System.Drawing.Font("Tahoma", 12F);
            this.nudDistK.Increment = new decimal(new int[] { 5, 0, 0, 65536 });
            this.nudDistK.Location = new System.Drawing.Point(330, 70);
            this.nudDistK.Maximum = new decimal(new int[] { 20, 0, 0, 0 });
            this.nudDistK.Name = "nudDistK";
            this.nudDistK.Size = new System.Drawing.Size(130, 34);
            this.nudDistK.TabIndex = 6;
            this.nudAngleK.Font = new System.Drawing.Font("Tahoma", 12F);
            this.nudAngleK.Increment = new decimal(new int[] { 5, 0, 0, 0 });
            this.nudAngleK.Location = new System.Drawing.Point(330, 120);
            this.nudAngleK.Maximum = new decimal(new int[] { 89, 0, 0, 0 });
            this.nudAngleK.Name = "nudAngleK";
            this.nudAngleK.Size = new System.Drawing.Size(130, 34);
            this.nudAngleK.TabIndex = 7;
            this.nudTransK.DecimalPlaces = 1;
            this.nudTransK.Font = new System.Drawing.Font("Tahoma", 12F);
            this.nudTransK.Increment = new decimal(new int[] { 1, 0, 0, 65536 });
            this.nudTransK.Location = new System.Drawing.Point(330, 170);
            this.nudTransK.Maximum = new decimal(new int[] { 10, 0, 0, 0 });
            this.nudTransK.Minimum = new decimal(new int[] { 1, 0, 0, 65536 });
            this.nudTransK.Name = "nudTransK";
            this.nudTransK.Size = new System.Drawing.Size(130, 34);
            this.nudTransK.TabIndex = 8;
            //
            // U labels + inputs
            //
            this.lblOutU.Font = new System.Drawing.Font("Tahoma", 11.25F, System.Drawing.FontStyle.Bold);
            this.lblOutU.Location = new System.Drawing.Point(4, 24);
            this.lblOutU.Name = "lblOutU";
            this.lblOutU.Size = new System.Drawing.Size(320, 30);
            this.lblOutU.TabIndex = 0;
            this.lblOutU.Text = "Exit radius";
            this.lblOutU.TextAlign = System.Drawing.ContentAlignment.MiddleLeft;
            this.lblInU.Font = new System.Drawing.Font("Tahoma", 11.25F, System.Drawing.FontStyle.Bold);
            this.lblInU.Location = new System.Drawing.Point(4, 74);
            this.lblInU.Name = "lblInU";
            this.lblInU.Size = new System.Drawing.Size(320, 30);
            this.lblInU.TabIndex = 1;
            this.lblInU.Text = "Entry radius";
            this.lblInU.TextAlign = System.Drawing.ContentAlignment.MiddleLeft;
            this.lblHintU.Font = new System.Drawing.Font("Tahoma", 9.75F, System.Drawing.FontStyle.Italic);
            this.lblHintU.ForeColor = System.Drawing.Color.DimGray;
            this.lblHintU.Location = new System.Drawing.Point(4, 190);
            this.lblHintU.Name = "lblHintU";
            this.lblHintU.Size = new System.Drawing.Size(452, 150);
            this.lblHintU.TabIndex = 2;
            this.lblHintU.Text = "Radius 0 = use general turn radius. A small entry radius makes the turn overshoot, then a short reverse aligns onto the line.";
            this.nudOutU.DecimalPlaces = 1;
            this.nudOutU.Font = new System.Drawing.Font("Tahoma", 12F);
            this.nudOutU.Increment = new decimal(new int[] { 1, 0, 0, 65536 });
            this.nudOutU.Location = new System.Drawing.Point(330, 20);
            this.nudOutU.Maximum = new decimal(new int[] { 60, 0, 0, 0 });
            this.nudOutU.Name = "nudOutU";
            this.nudOutU.Size = new System.Drawing.Size(130, 34);
            this.nudOutU.TabIndex = 3;
            this.nudInU.DecimalPlaces = 1;
            this.nudInU.Font = new System.Drawing.Font("Tahoma", 12F);
            this.nudInU.Increment = new decimal(new int[] { 1, 0, 0, 65536 });
            this.nudInU.Location = new System.Drawing.Point(330, 70);
            this.nudInU.Maximum = new decimal(new int[] { 60, 0, 0, 0 });
            this.nudInU.Name = "nudInU";
            this.nudInU.Size = new System.Drawing.Size(130, 34);
            this.nudInU.TabIndex = 4;
            //
            // RS labels + inputs
            //
            this.lblMinRS.Font = new System.Drawing.Font("Tahoma", 11.25F, System.Drawing.FontStyle.Bold);
            this.lblMinRS.Location = new System.Drawing.Point(4, 24);
            this.lblMinRS.Name = "lblMinRS";
            this.lblMinRS.Size = new System.Drawing.Size(320, 30);
            this.lblMinRS.TabIndex = 0;
            this.lblMinRS.Text = "Min radius";
            this.lblMinRS.TextAlign = System.Drawing.ContentAlignment.MiddleLeft;
            this.lblHintRS.Font = new System.Drawing.Font("Tahoma", 9.75F, System.Drawing.FontStyle.Italic);
            this.lblHintRS.ForeColor = System.Drawing.Color.DimGray;
            this.lblHintRS.Location = new System.Drawing.Point(4, 80);
            this.lblHintRS.Name = "lblHintRS";
            this.lblHintRS.Size = new System.Drawing.Size(452, 120);
            this.lblHintRS.TabIndex = 1;
            this.lblHintRS.Text = "Reeds-Shepp auto-solver - coming in a later step.";
            this.nudMinRS.DecimalPlaces = 1;
            this.nudMinRS.Enabled = false;
            this.nudMinRS.Font = new System.Drawing.Font("Tahoma", 12F);
            this.nudMinRS.Location = new System.Drawing.Point(330, 20);
            this.nudMinRS.Maximum = new decimal(new int[] { 60, 0, 0, 0 });
            this.nudMinRS.Name = "nudMinRS";
            this.nudMinRS.Size = new System.Drawing.Size(130, 34);
            this.nudMinRS.TabIndex = 2;
            //
            // lblTolerance (common - trigger tolerance, all styles)
            //
            this.lblTolerance.Font = new System.Drawing.Font("Tahoma", 11.25F, System.Drawing.FontStyle.Bold);
            this.lblTolerance.Location = new System.Drawing.Point(512, 22);
            this.lblTolerance.Name = "lblTolerance";
            this.lblTolerance.Size = new System.Drawing.Size(190, 30);
            this.lblTolerance.TabIndex = 6;
            this.lblTolerance.Text = "Trigger tolerance (m)";
            this.lblTolerance.TextAlign = System.Drawing.ContentAlignment.MiddleRight;
            //
            // nudTolerance
            //
            this.nudTolerance.DecimalPlaces = 1;
            this.nudTolerance.Font = new System.Drawing.Font("Tahoma", 12F);
            this.nudTolerance.Increment = new decimal(new int[] { 5, 0, 0, 65536 });
            this.nudTolerance.Location = new System.Drawing.Point(712, 18);
            this.nudTolerance.Maximum = new decimal(new int[] { 10, 0, 0, 0 });
            this.nudTolerance.Minimum = new decimal(new int[] { 2, 0, 0, 65536 });
            this.nudTolerance.Name = "nudTolerance";
            this.nudTolerance.Size = new System.Drawing.Size(110, 34);
            this.nudTolerance.TabIndex = 7;
            //
            // lblOvershootU
            //
            this.lblOvershootU.Font = new System.Drawing.Font("Tahoma", 11.25F, System.Drawing.FontStyle.Bold);
            this.lblOvershootU.Location = new System.Drawing.Point(4, 124);
            this.lblOvershootU.Name = "lblOvershootU";
            this.lblOvershootU.Size = new System.Drawing.Size(320, 30);
            this.lblOvershootU.TabIndex = 5;
            this.lblOvershootU.Text = "Overshoot distance (m)";
            this.lblOvershootU.TextAlign = System.Drawing.ContentAlignment.MiddleLeft;
            //
            // nudOvershootU
            //
            this.nudOvershootU.DecimalPlaces = 1;
            this.nudOvershootU.Font = new System.Drawing.Font("Tahoma", 12F);
            this.nudOvershootU.Increment = new decimal(new int[] { 5, 0, 0, 65536 });
            this.nudOvershootU.Location = new System.Drawing.Point(330, 120);
            this.nudOvershootU.Maximum = new decimal(new int[] { 10, 0, 0, 0 });
            this.nudOvershootU.Name = "nudOvershootU";
            this.nudOvershootU.Size = new System.Drawing.Size(130, 34);
            this.nudOvershootU.TabIndex = 6;
            //
            // FormTurnLine
            //
            this.AutoScaleDimensions = new System.Drawing.SizeF(6F, 13F);
            this.AutoScaleMode = System.Windows.Forms.AutoScaleMode.Font;
            this.BackColor = System.Drawing.Color.WhiteSmoke;
            this.ClientSize = new System.Drawing.Size(1040, 684);
            this.Controls.Add(this.pnlPath);
            this.Controls.Add(this.pnlParams);
            this.Controls.Add(this.btnTabMap);
            this.Controls.Add(this.btnTabParams);
            this.Controls.Add(this.btnTabPath);
            this.Controls.Add(this.btnWinFull);
            this.Controls.Add(this.btnWinClose);
            this.Controls.Add(this.btnZoomIn);
            this.Controls.Add(this.btnZoomOut);
            this.Controls.Add(this.lblStatus);
            this.Controls.Add(this.btnSave);
            this.Controls.Add(this.chkCombined);
            this.Controls.Add(this.chkKPrimary);
            this.Controls.Add(this.btnGoToField);
            this.Controls.Add(this.chkParcels);
            this.Controls.Add(this.btnDeleteLine);
            this.Controls.Add(this.btnDeletePoint);
            this.Controls.Add(this.btnAddPoint);
            this.Controls.Add(this.btnAddLine);
            this.Controls.Add(this.lblRadius);
            this.Controls.Add(this.chkAfterStop);
            this.Controls.Add(this.chkAutomatic);
            this.Controls.Add(this.chkActivate);
            this.Controls.Add(this.gMapControl);
            this.Font = new System.Drawing.Font("Tahoma", 8.25F);
            this.FormBorderStyle = System.Windows.Forms.FormBorderStyle.None;
            this.MinimumSize = new System.Drawing.Size(860, 560);
            this.Name = "FormTurnLine";
            this.StartPosition = System.Windows.Forms.FormStartPosition.CenterParent;
            this.Text = "Turn Lines";
            this.Load += new System.EventHandler(this.FormTurnLine_Load);
            this.Shown += new System.EventHandler(this.FormTurnLine_Shown);
            this.FormClosing += new System.Windows.Forms.FormClosingEventHandler(this.FormTurnLine_FormClosing);
            ((System.ComponentModel.ISupportInitialize)(this.nudExitK)).EndInit();
            ((System.ComponentModel.ISupportInitialize)(this.nudDistK)).EndInit();
            ((System.ComponentModel.ISupportInitialize)(this.nudAngleK)).EndInit();
            ((System.ComponentModel.ISupportInitialize)(this.nudTransK)).EndInit();
            ((System.ComponentModel.ISupportInitialize)(this.nudOutU)).EndInit();
            ((System.ComponentModel.ISupportInitialize)(this.nudInU)).EndInit();
            ((System.ComponentModel.ISupportInitialize)(this.nudMinRS)).EndInit();
            ((System.ComponentModel.ISupportInitialize)(this.nudTolerance)).EndInit();
            ((System.ComponentModel.ISupportInitialize)(this.nudOvershootU)).EndInit();
            ((System.ComponentModel.ISupportInitialize)(this.nudZones)).EndInit();
            this.ResumeLayout(false);
        }

        #endregion

        private System.Windows.Forms.CheckBox chkActivate;
        private System.Windows.Forms.CheckBox chkAutomatic;
        private System.Windows.Forms.CheckBox chkAfterStop;
        private System.Windows.Forms.Label lblRadius;
        private System.Windows.Forms.Button btnAddLine;
        private System.Windows.Forms.CheckBox btnAddPoint;
        private System.Windows.Forms.CheckBox btnDeletePoint;
        private System.Windows.Forms.CheckBox btnDeleteLine;
        private System.Windows.Forms.CheckBox chkParcels;
        private System.Windows.Forms.Button btnGoToField;
        private System.Windows.Forms.CheckBox chkKPrimary;
        private System.Windows.Forms.CheckBox chkCombined;
        private System.Windows.Forms.Button btnSave;
        private System.Windows.Forms.Label lblStatus;
        private GMap.NET.WindowsForms.GMapControl gMapControl;
        private System.Windows.Forms.Button btnZoomIn;
        private System.Windows.Forms.Button btnZoomOut;
        private System.Windows.Forms.Button btnWinFull;
        private System.Windows.Forms.Button btnWinClose;
        private System.Windows.Forms.Button btnTabMap;
        private System.Windows.Forms.Button btnTabParams;
        private System.Windows.Forms.Button btnTabPath;
        private System.Windows.Forms.Panel pnlParams;
        private System.Windows.Forms.Panel pnlPath;
        private System.Windows.Forms.Label lblTrack;
        private System.Windows.Forms.ComboBox cboTrack;
        private System.Windows.Forms.CheckBox btnMode1;
        private System.Windows.Forms.CheckBox btnMode2;
        private System.Windows.Forms.CheckBox btnMode3;
        private System.Windows.Forms.Label lblZones;
        private System.Windows.Forms.NumericUpDown nudZones;
        private System.Windows.Forms.CheckBox chkHeadlandClip;
        private System.Windows.Forms.Label lblPlanInfo;
        private System.Windows.Forms.Button btnGeneratePlan;
        private System.Windows.Forms.Button btnAddPath;
        private System.Windows.Forms.Button btnDeletePath;
        private System.Windows.Forms.CheckBox chkFollowPlan;
        private System.Windows.Forms.Label lblPlanStatus;
        private System.Windows.Forms.CheckBox btnStyleK;
        private System.Windows.Forms.CheckBox btnStyleU;
        private System.Windows.Forms.CheckBox btnStyleRS;
        private System.Windows.Forms.Panel pnlK;
        private System.Windows.Forms.Panel pnlU;
        private System.Windows.Forms.Panel pnlRS;
        private System.Windows.Forms.Label lblExitK;
        private System.Windows.Forms.Label lblDistK;
        private System.Windows.Forms.Label lblAngleK;
        private System.Windows.Forms.Label lblTransK;
        private System.Windows.Forms.Label lblOutU;
        private System.Windows.Forms.Label lblInU;
        private System.Windows.Forms.Label lblMinRS;
        private System.Windows.Forms.Label lblHintK;
        private System.Windows.Forms.Label lblHintU;
        private System.Windows.Forms.Label lblHintRS;
        private System.Windows.Forms.NumericUpDown nudExitK;
        private System.Windows.Forms.NumericUpDown nudDistK;
        private System.Windows.Forms.NumericUpDown nudAngleK;
        private System.Windows.Forms.NumericUpDown nudTransK;
        private System.Windows.Forms.NumericUpDown nudOutU;
        private System.Windows.Forms.NumericUpDown nudInU;
        private System.Windows.Forms.NumericUpDown nudMinRS;
        private System.Windows.Forms.Label lblTolerance;
        private System.Windows.Forms.NumericUpDown nudTolerance;
        private System.Windows.Forms.Label lblOvershootU;
        private System.Windows.Forms.NumericUpDown nudOvershootU;
    }
}
