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
            this.btnSave = new System.Windows.Forms.Button();
            this.lblRadius = new System.Windows.Forms.Label();
            this.btnTabPath = new System.Windows.Forms.Button();
            this.btnTabParams = new System.Windows.Forms.Button();
            this.btnTabMap = new System.Windows.Forms.Button();
            this.btnResetPath = new System.Windows.Forms.Button();
            this.lblStatus = new System.Windows.Forms.Label();
            this.gMapControl = new GMap.NET.WindowsForms.GMapControl();
            this.btnZoomIn = new System.Windows.Forms.Button();
            this.btnZoomOut = new System.Windows.Forms.Button();
            this.btnMeasure = new System.Windows.Forms.Button();
            this.lblMeasure = new System.Windows.Forms.Label();
            this.btnWinFull = new System.Windows.Forms.Button();
            this.btnWinClose = new System.Windows.Forms.Button();
            this.pnlMapTools = new System.Windows.Forms.Panel();
            this.btnSaveLines = new System.Windows.Forms.Button();
            this.btnAddLine = new System.Windows.Forms.Button();
            this.btnBorderLine = new System.Windows.Forms.CheckBox();
            this.btnAddPoint = new System.Windows.Forms.CheckBox();
            this.btnDeletePoint = new System.Windows.Forms.CheckBox();
            this.btnDeleteLine = new System.Windows.Forms.CheckBox();
            this.chkParcels = new System.Windows.Forms.CheckBox();
            this.btnGoToField = new System.Windows.Forms.Button();
            this.pnlParams = new System.Windows.Forms.Panel();
            this.pnlPath = new System.Windows.Forms.Panel();
            this.lblWizTitle = new System.Windows.Forms.Label();
            this.lstLines = new System.Windows.Forms.ListBox();
            this.lblTrack = new System.Windows.Forms.Label();
            this.cboTrack = new System.Windows.Forms.ComboBox();
            this.btnMode1 = new System.Windows.Forms.CheckBox();
            this.btnMode2 = new System.Windows.Forms.CheckBox();
            this.btnMode3 = new System.Windows.Forms.CheckBox();
            this.lblZones = new System.Windows.Forms.Label();
            this.lblZoneOrderRead = new System.Windows.Forms.Label();
            this.nudZones = new System.Windows.Forms.NumericUpDown();
            this.chkHeadlandClip = new System.Windows.Forms.CheckBox();
            this.lblPlanInfo = new System.Windows.Forms.Label();
            this.btnGeneratePlan = new System.Windows.Forms.Button();
            this.btnAddPath = new System.Windows.Forms.Button();
            this.btnDeletePath = new System.Windows.Forms.Button();
            this.lblPlanStatus = new System.Windows.Forms.Label();
            this.btnWizBack = new System.Windows.Forms.Button();
            this.btnWizNext = new System.Windows.Forms.Button();
            this.btnWizCancel = new System.Windows.Forms.Button();
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
            this.lblBndOffset = new System.Windows.Forms.Label();
            this.btnApplyParams = new System.Windows.Forms.Button();
            this.nudBndOffset = new AgOpenGPS.NudlessNumericUpDown();
            this.nudTolerance = new System.Windows.Forms.NumericUpDown();
            this.lblToleranceAfter = new System.Windows.Forms.Label();
            this.nudToleranceAfter = new System.Windows.Forms.NumericUpDown();
            this.lblOvershootU = new System.Windows.Forms.Label();
            this.nudOvershootU = new System.Windows.Forms.NumericUpDown();
            this.SuspendLayout();
            //
            // ── ROW 1: activate (3-state), trigger mode (2-state), OK ──
            //
            this.chkActivate.Appearance = System.Windows.Forms.Appearance.Button;
            this.chkActivate.BackColor = System.Drawing.Color.AliceBlue;
            this.chkActivate.FlatStyle = System.Windows.Forms.FlatStyle.Flat;
            this.chkActivate.Font = new System.Drawing.Font("Tahoma", 12F, System.Drawing.FontStyle.Bold);
            this.chkActivate.Location = new System.Drawing.Point(12, 8);
            this.chkActivate.Name = "chkActivate";
            this.chkActivate.Size = new System.Drawing.Size(220, 48);
            this.chkActivate.TabIndex = 0;
            this.chkActivate.Text = "Activate turn";
            this.chkActivate.TextAlign = System.Drawing.ContentAlignment.MiddleCenter;
            this.chkActivate.UseVisualStyleBackColor = false;
            //
            this.chkAutomatic.Appearance = System.Windows.Forms.Appearance.Button;
            this.chkAutomatic.BackColor = System.Drawing.Color.MediumAquamarine;
            this.chkAutomatic.FlatStyle = System.Windows.Forms.FlatStyle.Flat;
            this.chkAutomatic.Font = new System.Drawing.Font("Tahoma", 10F, System.Drawing.FontStyle.Bold);
            this.chkAutomatic.Location = new System.Drawing.Point(240, 8);
            this.chkAutomatic.Name = "chkAutomatic";
            this.chkAutomatic.Size = new System.Drawing.Size(200, 48);
            this.chkAutomatic.TabIndex = 1;
            this.chkAutomatic.Text = "Automatic turn";
            this.chkAutomatic.TextAlign = System.Drawing.ContentAlignment.MiddleCenter;
            this.chkAutomatic.UseVisualStyleBackColor = false;
            //
            this.btnSave.BackColor = System.Drawing.Color.Honeydew;
            this.btnSave.FlatStyle = System.Windows.Forms.FlatStyle.Flat;
            this.btnSave.Font = new System.Drawing.Font("Tahoma", 12F, System.Drawing.FontStyle.Bold);
            this.btnSave.Location = new System.Drawing.Point(448, 8);
            this.btnSave.Name = "btnSave";
            this.btnSave.Size = new System.Drawing.Size(150, 48);
            this.btnSave.TabIndex = 2;
            this.btnSave.Text = "OK";
            this.btnSave.UseVisualStyleBackColor = false;
            this.btnSave.Click += new System.EventHandler(this.btnSave_Click);
            //
            this.lblRadius.Font = new System.Drawing.Font("Tahoma", 11.25F, System.Drawing.FontStyle.Bold);
            this.lblRadius.ForeColor = System.Drawing.Color.DimGray;
            this.lblRadius.Location = new System.Drawing.Point(614, 18);
            this.lblRadius.Name = "lblRadius";
            this.lblRadius.Size = new System.Drawing.Size(250, 30);
            this.lblRadius.TabIndex = 3;
            this.lblRadius.TextAlign = System.Drawing.ContentAlignment.MiddleLeft;
            //
            // ── ROW 2: view buttons (Path / Settings / Map) + Reset path ──
            //
            this.btnTabPath.BackColor = System.Drawing.Color.Gold;
            this.btnTabPath.BackgroundImage = global::AgOpenGPS.Properties.Resources.ABTracks;
            this.btnTabPath.BackgroundImageLayout = System.Windows.Forms.ImageLayout.Zoom;
            this.btnTabPath.FlatStyle = System.Windows.Forms.FlatStyle.Flat;
            this.btnTabPath.Location = new System.Drawing.Point(12, 62);
            this.btnTabPath.Name = "btnTabPath";
            this.btnTabPath.Size = new System.Drawing.Size(96, 44);
            this.btnTabPath.TabIndex = 4;
            this.btnTabPath.UseVisualStyleBackColor = false;
            this.btnTabPath.Click += new System.EventHandler(this.btnTabPath_Click);
            //
            this.btnTabParams.BackColor = System.Drawing.Color.Lavender;
            this.btnTabParams.BackgroundImage = global::AgOpenGPS.Properties.Resources.Settings48;
            this.btnTabParams.BackgroundImageLayout = System.Windows.Forms.ImageLayout.Zoom;
            this.btnTabParams.FlatStyle = System.Windows.Forms.FlatStyle.Flat;
            this.btnTabParams.Location = new System.Drawing.Point(124, 62);
            this.btnTabParams.Name = "btnTabParams";
            this.btnTabParams.Size = new System.Drawing.Size(96, 44);
            this.btnTabParams.TabIndex = 5;
            this.btnTabParams.UseVisualStyleBackColor = false;
            this.btnTabParams.Click += new System.EventHandler(this.btnTabParams_Click);
            //
            this.btnTabMap.BackColor = System.Drawing.Color.Lavender;
            this.btnTabMap.BackgroundImage = global::AgOpenGPS.Properties.Resources.MappingOn;
            this.btnTabMap.BackgroundImageLayout = System.Windows.Forms.ImageLayout.Zoom;
            this.btnTabMap.FlatStyle = System.Windows.Forms.FlatStyle.Flat;
            this.btnTabMap.Location = new System.Drawing.Point(236, 62);
            this.btnTabMap.Name = "btnTabMap";
            this.btnTabMap.Size = new System.Drawing.Size(96, 44);
            this.btnTabMap.TabIndex = 6;
            this.btnTabMap.UseVisualStyleBackColor = false;
            this.btnTabMap.Click += new System.EventHandler(this.btnTabMap_Click);
            //
            this.btnResetPath.BackColor = System.Drawing.Color.MistyRose;
            this.btnResetPath.FlatStyle = System.Windows.Forms.FlatStyle.Flat;
            this.btnResetPath.Font = new System.Drawing.Font("Tahoma", 10F, System.Drawing.FontStyle.Bold);
            this.btnResetPath.Location = new System.Drawing.Point(372, 62);
            this.btnResetPath.Name = "btnResetPath";
            this.btnResetPath.Size = new System.Drawing.Size(150, 44);
            this.btnResetPath.TabIndex = 7;
            this.btnResetPath.Text = "Reset path";
            this.btnResetPath.UseVisualStyleBackColor = false;
            this.btnResetPath.Click += new System.EventHandler(this.btnResetPath_Click);
            //
            this.lblStatus.Anchor = ((System.Windows.Forms.AnchorStyles)(((System.Windows.Forms.AnchorStyles.Top | System.Windows.Forms.AnchorStyles.Left)
                        | System.Windows.Forms.AnchorStyles.Right)));
            this.lblStatus.Font = new System.Drawing.Font("Tahoma", 9.75F, System.Drawing.FontStyle.Bold);
            this.lblStatus.ForeColor = System.Drawing.Color.DimGray;
            this.lblStatus.Location = new System.Drawing.Point(536, 62);
            this.lblStatus.Name = "lblStatus";
            this.lblStatus.Size = new System.Drawing.Size(360, 48);
            this.lblStatus.TabIndex = 8;
            this.lblStatus.TextAlign = System.Drawing.ContentAlignment.MiddleLeft;
            //
            // ── map area (y=116) ──
            //
            this.gMapControl.Anchor = ((System.Windows.Forms.AnchorStyles)((((System.Windows.Forms.AnchorStyles.Top | System.Windows.Forms.AnchorStyles.Bottom)
                        | System.Windows.Forms.AnchorStyles.Left)
                        | System.Windows.Forms.AnchorStyles.Right)));
            this.gMapControl.Bearing = 0F;
            this.gMapControl.CanDragMap = true;
            this.gMapControl.GrayScaleMode = false;
            this.gMapControl.Location = new System.Drawing.Point(12, 116);
            this.gMapControl.MarkersEnabled = true;
            this.gMapControl.MaxZoom = 22;
            this.gMapControl.MinZoom = 2;
            this.gMapControl.MouseWheelZoomType = GMap.NET.MouseWheelZoomType.MousePositionAndCenter;
            this.gMapControl.Name = "gMapControl";
            this.gMapControl.NegativeMode = false;
            this.gMapControl.PolygonsEnabled = true;
            this.gMapControl.RoutesEnabled = true;
            this.gMapControl.ShowTileGridLines = false;
            this.gMapControl.Size = new System.Drawing.Size(1016, 556);
            this.gMapControl.TabIndex = 9;
            this.gMapControl.Zoom = 0D;
            //
            this.btnZoomIn.Anchor = ((System.Windows.Forms.AnchorStyles)((System.Windows.Forms.AnchorStyles.Top | System.Windows.Forms.AnchorStyles.Right)));
            this.btnZoomIn.BackColor = System.Drawing.Color.White;
            this.btnZoomIn.Font = new System.Drawing.Font("Tahoma", 20F, System.Drawing.FontStyle.Bold);
            this.btnZoomIn.Location = new System.Drawing.Point(960, 126);
            this.btnZoomIn.Name = "btnZoomIn";
            this.btnZoomIn.Size = new System.Drawing.Size(56, 56);
            this.btnZoomIn.TabIndex = 10;
            this.btnZoomIn.Text = "+";
            this.btnZoomIn.UseVisualStyleBackColor = false;
            this.btnZoomIn.Click += new System.EventHandler(this.btnZoomIn_Click);
            //
            this.btnZoomOut.Anchor = ((System.Windows.Forms.AnchorStyles)((System.Windows.Forms.AnchorStyles.Top | System.Windows.Forms.AnchorStyles.Right)));
            this.btnZoomOut.BackColor = System.Drawing.Color.White;
            this.btnZoomOut.Font = new System.Drawing.Font("Tahoma", 20F, System.Drawing.FontStyle.Bold);
            this.btnZoomOut.Location = new System.Drawing.Point(960, 188);
            this.btnZoomOut.Name = "btnZoomOut";
            this.btnZoomOut.Size = new System.Drawing.Size(56, 56);
            this.btnZoomOut.TabIndex = 11;
            this.btnZoomOut.Text = "−";
            this.btnZoomOut.UseVisualStyleBackColor = false;
            this.btnZoomOut.Click += new System.EventHandler(this.btnZoomOut_Click);
            //
            // btnMeasure
            //
            this.btnMeasure.Anchor = ((System.Windows.Forms.AnchorStyles)((System.Windows.Forms.AnchorStyles.Bottom | System.Windows.Forms.AnchorStyles.Right)));
            this.btnMeasure.BackColor = System.Drawing.Color.White;
            this.btnMeasure.Font = new System.Drawing.Font("Tahoma", 11.25F, System.Drawing.FontStyle.Bold);
            this.btnMeasure.Location = new System.Drawing.Point(74, 500);
            this.btnMeasure.Name = "btnMeasure";
            this.btnMeasure.Size = new System.Drawing.Size(110, 46);
            this.btnMeasure.TabIndex = 20;
            this.btnMeasure.Text = "Measure";
            this.btnMeasure.UseVisualStyleBackColor = false;
            this.btnMeasure.Click += new System.EventHandler(this.btnMeasure_Click);
            //
            // lblMeasure
            //
            this.lblMeasure.Anchor = ((System.Windows.Forms.AnchorStyles)((System.Windows.Forms.AnchorStyles.Bottom | System.Windows.Forms.AnchorStyles.Right)));
            this.lblMeasure.Font = new System.Drawing.Font("Tahoma", 12F, System.Drawing.FontStyle.Bold);
            this.lblMeasure.Location = new System.Drawing.Point(74, 472);
            this.lblMeasure.Name = "lblMeasure";
            this.lblMeasure.Size = new System.Drawing.Size(110, 26);
            this.lblMeasure.TabIndex = 21;
            this.lblMeasure.TextAlign = System.Drawing.ContentAlignment.MiddleCenter;
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
            this.btnWinFull.TabIndex = 12;
            this.btnWinFull.UseVisualStyleBackColor = false;
            this.btnWinFull.Click += new System.EventHandler(this.btnWinFull_Click);
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
            this.btnWinClose.TabIndex = 13;
            this.btnWinClose.UseVisualStyleBackColor = false;
            this.btnWinClose.Click += new System.EventHandler(this.btnWinClose_Click);
            //
            // ── MAP view: left tool column (turn-line editing lives here now) ──
            //
            this.pnlMapTools.Anchor = ((System.Windows.Forms.AnchorStyles)(((System.Windows.Forms.AnchorStyles.Top | System.Windows.Forms.AnchorStyles.Bottom)
                        | System.Windows.Forms.AnchorStyles.Left)));
            this.pnlMapTools.BackColor = System.Drawing.Color.WhiteSmoke;
            this.pnlMapTools.BorderStyle = System.Windows.Forms.BorderStyle.FixedSingle;
            this.pnlMapTools.Location = new System.Drawing.Point(12, 116);
            this.pnlMapTools.Name = "pnlMapTools";
            this.pnlMapTools.Size = new System.Drawing.Size(192, 556);
            this.pnlMapTools.TabIndex = 14;
            this.pnlMapTools.Visible = false;
            this.pnlMapTools.Controls.Add(this.btnSaveLines);
            this.pnlMapTools.Controls.Add(this.btnAddLine);
            this.pnlMapTools.Controls.Add(this.btnBorderLine);
            this.pnlMapTools.Controls.Add(this.btnAddPoint);
            this.pnlMapTools.Controls.Add(this.btnDeletePoint);
            this.pnlMapTools.Controls.Add(this.btnDeleteLine);
            this.pnlMapTools.Controls.Add(this.chkParcels);
            this.pnlMapTools.Controls.Add(this.btnGoToField);
            this.pnlMapTools.Controls.Add(this.lblMeasure);
            this.pnlMapTools.Controls.Add(this.btnMeasure);
            //
            this.btnSaveLines.BackColor = System.Drawing.Color.Honeydew;
            this.btnSaveLines.FlatStyle = System.Windows.Forms.FlatStyle.Flat;
            this.btnSaveLines.Font = new System.Drawing.Font("Tahoma", 11.25F, System.Drawing.FontStyle.Bold);
            this.btnSaveLines.Location = new System.Drawing.Point(10, 10);
            this.btnSaveLines.Name = "btnSaveLines";
            this.btnSaveLines.Size = new System.Drawing.Size(170, 46);
            this.btnSaveLines.TabIndex = 0;
            this.btnSaveLines.Text = "Save";
            this.btnSaveLines.UseVisualStyleBackColor = false;
            this.btnSaveLines.Click += new System.EventHandler(this.btnSaveLines_Click);
            //
            this.btnAddLine.BackColor = System.Drawing.Color.PapayaWhip;
            this.btnAddLine.FlatStyle = System.Windows.Forms.FlatStyle.Flat;
            this.btnAddLine.Font = new System.Drawing.Font("Tahoma", 11.25F, System.Drawing.FontStyle.Bold);
            this.btnAddLine.Location = new System.Drawing.Point(10, 62);
            this.btnAddLine.Name = "btnAddLine";
            this.btnAddLine.Size = new System.Drawing.Size(170, 46);
            this.btnAddLine.TabIndex = 0;
            this.btnAddLine.Text = "Add line";
            this.btnAddLine.UseVisualStyleBackColor = false;
            this.btnAddLine.Click += new System.EventHandler(this.btnAddLine_Click);
            //
            this.btnBorderLine.Appearance = System.Windows.Forms.Appearance.Button;
            this.btnBorderLine.BackColor = System.Drawing.Color.PapayaWhip;
            this.btnBorderLine.FlatAppearance.CheckedBackColor = System.Drawing.Color.Gold;
            this.btnBorderLine.FlatStyle = System.Windows.Forms.FlatStyle.Flat;
            this.btnBorderLine.Font = new System.Drawing.Font("Tahoma", 10F, System.Drawing.FontStyle.Bold);
            this.btnBorderLine.Location = new System.Drawing.Point(10, 114);
            this.btnBorderLine.Name = "btnBorderLine";
            this.btnBorderLine.Size = new System.Drawing.Size(170, 46);
            this.btnBorderLine.TabIndex = 2;
            this.btnBorderLine.Text = "Add line on border";
            this.btnBorderLine.TextAlign = System.Drawing.ContentAlignment.MiddleCenter;
            this.btnBorderLine.UseVisualStyleBackColor = false;
            this.btnBorderLine.CheckedChanged += new System.EventHandler(this.btnBorderLine_CheckedChanged);
            //
            this.btnAddPoint.Appearance = System.Windows.Forms.Appearance.Button;
            this.btnAddPoint.BackColor = System.Drawing.Color.AliceBlue;
            this.btnAddPoint.FlatAppearance.CheckedBackColor = System.Drawing.Color.Gold;
            this.btnAddPoint.FlatStyle = System.Windows.Forms.FlatStyle.Flat;
            this.btnAddPoint.Font = new System.Drawing.Font("Tahoma", 10F, System.Drawing.FontStyle.Bold);
            this.btnAddPoint.Location = new System.Drawing.Point(10, 176);
            this.btnAddPoint.Name = "btnAddPoint";
            this.btnAddPoint.Size = new System.Drawing.Size(170, 46);
            this.btnAddPoint.TabIndex = 1;
            this.btnAddPoint.Text = "Add point";
            this.btnAddPoint.TextAlign = System.Drawing.ContentAlignment.MiddleCenter;
            this.btnAddPoint.UseVisualStyleBackColor = false;
            this.btnAddPoint.CheckedChanged += new System.EventHandler(this.btnAddPoint_CheckedChanged);
            //
            this.btnDeletePoint.Appearance = System.Windows.Forms.Appearance.Button;
            this.btnDeletePoint.BackColor = System.Drawing.Color.AliceBlue;
            this.btnDeletePoint.FlatAppearance.CheckedBackColor = System.Drawing.Color.IndianRed;
            this.btnDeletePoint.FlatStyle = System.Windows.Forms.FlatStyle.Flat;
            this.btnDeletePoint.Font = new System.Drawing.Font("Tahoma", 10F, System.Drawing.FontStyle.Bold);
            this.btnDeletePoint.Location = new System.Drawing.Point(10, 228);
            this.btnDeletePoint.Name = "btnDeletePoint";
            this.btnDeletePoint.Size = new System.Drawing.Size(170, 46);
            this.btnDeletePoint.TabIndex = 2;
            this.btnDeletePoint.Text = "Delete point";
            this.btnDeletePoint.TextAlign = System.Drawing.ContentAlignment.MiddleCenter;
            this.btnDeletePoint.UseVisualStyleBackColor = false;
            this.btnDeletePoint.CheckedChanged += new System.EventHandler(this.btnDeletePoint_CheckedChanged);
            //
            this.btnDeleteLine.Appearance = System.Windows.Forms.Appearance.Button;
            this.btnDeleteLine.BackColor = System.Drawing.Color.AliceBlue;
            this.btnDeleteLine.FlatAppearance.CheckedBackColor = System.Drawing.Color.IndianRed;
            this.btnDeleteLine.FlatStyle = System.Windows.Forms.FlatStyle.Flat;
            this.btnDeleteLine.Font = new System.Drawing.Font("Tahoma", 10F, System.Drawing.FontStyle.Bold);
            this.btnDeleteLine.Location = new System.Drawing.Point(10, 280);
            this.btnDeleteLine.Name = "btnDeleteLine";
            this.btnDeleteLine.Size = new System.Drawing.Size(170, 46);
            this.btnDeleteLine.TabIndex = 3;
            this.btnDeleteLine.Text = "Delete line";
            this.btnDeleteLine.TextAlign = System.Drawing.ContentAlignment.MiddleCenter;
            this.btnDeleteLine.UseVisualStyleBackColor = false;
            this.btnDeleteLine.CheckedChanged += new System.EventHandler(this.btnDeleteLine_CheckedChanged);
            //
            this.chkParcels.Appearance = System.Windows.Forms.Appearance.Button;
            this.chkParcels.BackColor = System.Drawing.Color.AliceBlue;
            this.chkParcels.Checked = true;
            this.chkParcels.CheckState = System.Windows.Forms.CheckState.Checked;
            this.chkParcels.FlatAppearance.CheckedBackColor = System.Drawing.Color.MediumAquamarine;
            this.chkParcels.FlatStyle = System.Windows.Forms.FlatStyle.Flat;
            this.chkParcels.Font = new System.Drawing.Font("Tahoma", 10F, System.Drawing.FontStyle.Bold);
            this.chkParcels.Location = new System.Drawing.Point(10, 342);
            this.chkParcels.Name = "chkParcels";
            this.chkParcels.Size = new System.Drawing.Size(170, 46);
            this.chkParcels.TabIndex = 4;
            this.chkParcels.Text = "Cadastral parcels";
            this.chkParcels.TextAlign = System.Drawing.ContentAlignment.MiddleCenter;
            this.chkParcels.UseVisualStyleBackColor = false;
            this.chkParcels.CheckedChanged += new System.EventHandler(this.chkParcels_CheckedChanged);
            //
            this.btnGoToField.Font = new System.Drawing.Font("Tahoma", 10F, System.Drawing.FontStyle.Bold);
            this.btnGoToField.Location = new System.Drawing.Point(10, 394);
            this.btnGoToField.Name = "btnGoToField";
            this.btnGoToField.Size = new System.Drawing.Size(170, 46);
            this.btnGoToField.TabIndex = 5;
            this.btnGoToField.Text = "Go to position";
            this.btnGoToField.UseVisualStyleBackColor = true;
            this.btnGoToField.Click += new System.EventHandler(this.btnGoToField_Click);
            //
            // ── SETTINGS view (unchanged content) ──
            //
            this.pnlParams.Anchor = ((System.Windows.Forms.AnchorStyles)((((System.Windows.Forms.AnchorStyles.Top | System.Windows.Forms.AnchorStyles.Bottom)
                        | System.Windows.Forms.AnchorStyles.Left)
                        | System.Windows.Forms.AnchorStyles.Right)));
            this.pnlParams.BackColor = System.Drawing.Color.WhiteSmoke;
            this.pnlParams.BorderStyle = System.Windows.Forms.BorderStyle.FixedSingle;
            this.pnlParams.Location = new System.Drawing.Point(12, 116);
            this.pnlParams.Name = "pnlParams";
            this.pnlParams.Size = new System.Drawing.Size(1016, 556);
            this.pnlParams.TabIndex = 15;
            this.pnlParams.Visible = false;
            this.pnlParams.Controls.Add(this.btnStyleK);
            this.pnlParams.Controls.Add(this.btnStyleU);
            this.pnlParams.Controls.Add(this.btnStyleRS);
            this.pnlParams.Controls.Add(this.btnApplyParams);
            this.pnlParams.Controls.Add(this.lblBndOffset);
            this.pnlParams.Controls.Add(this.nudBndOffset);
            this.pnlParams.Controls.Add(this.lblTolerance);
            this.pnlParams.Controls.Add(this.nudTolerance);
            this.pnlParams.Controls.Add(this.lblToleranceAfter);
            this.pnlParams.Controls.Add(this.nudToleranceAfter);
            this.pnlParams.Controls.Add(this.pnlK);
            this.pnlParams.Controls.Add(this.pnlU);
            this.pnlParams.Controls.Add(this.pnlRS);
            //
            // ── PATH PLANNING view (default) — left wizard strip over the map ──
            //
            this.pnlPath.Anchor = ((System.Windows.Forms.AnchorStyles)(((System.Windows.Forms.AnchorStyles.Top | System.Windows.Forms.AnchorStyles.Bottom)
                        | System.Windows.Forms.AnchorStyles.Left)));
            this.pnlPath.BackColor = System.Drawing.Color.WhiteSmoke;
            this.pnlPath.BorderStyle = System.Windows.Forms.BorderStyle.FixedSingle;
            this.pnlPath.Location = new System.Drawing.Point(12, 116);
            this.pnlPath.Name = "pnlPath";
            this.pnlPath.Size = new System.Drawing.Size(348, 556);
            this.pnlPath.TabIndex = 16;
            this.pnlPath.Visible = false;
            this.pnlPath.Controls.Add(this.lblWizTitle);
            this.pnlPath.Controls.Add(this.lstLines);
            this.pnlPath.Controls.Add(this.lblTrack);
            this.pnlPath.Controls.Add(this.cboTrack);
            this.pnlPath.Controls.Add(this.btnMode1);
            this.pnlPath.Controls.Add(this.btnMode3);
            this.pnlPath.Controls.Add(this.btnMode2);
            this.pnlPath.Controls.Add(this.lblZones);
            this.pnlPath.Controls.Add(this.nudZones);
            this.pnlPath.Controls.Add(this.lblZoneOrderRead);
            this.pnlPath.Controls.Add(this.chkHeadlandClip);
            this.pnlPath.Controls.Add(this.lblPlanInfo);
            this.pnlPath.Controls.Add(this.btnGeneratePlan);
            this.pnlPath.Controls.Add(this.btnAddPath);
            this.pnlPath.Controls.Add(this.btnDeletePath);
            this.pnlPath.Controls.Add(this.lblPlanStatus);
            this.pnlPath.Controls.Add(this.btnWizBack);
            this.pnlPath.Controls.Add(this.btnWizNext);
            this.pnlPath.Controls.Add(this.btnWizCancel);
            //
            this.lblWizTitle.Font = new System.Drawing.Font("Tahoma", 11.25F, System.Drawing.FontStyle.Bold);
            this.lblWizTitle.Location = new System.Drawing.Point(10, 8);
            this.lblWizTitle.Name = "lblWizTitle";
            this.lblWizTitle.Size = new System.Drawing.Size(326, 52);
            this.lblWizTitle.TabIndex = 0;
            this.lblWizTitle.TextAlign = System.Drawing.ContentAlignment.MiddleLeft;
            //
            this.lstLines.Font = new System.Drawing.Font("Tahoma", 12F);
            this.lstLines.HorizontalScrollbar = true;
            this.lstLines.ItemHeight = 32;
            this.lstLines.Location = new System.Drawing.Point(10, 64);
            this.lstLines.Name = "lstLines";
            this.lstLines.Size = new System.Drawing.Size(326, 260);
            this.lstLines.TabIndex = 1;
            this.lstLines.Visible = false;
            //
            // legacy line combo: hidden, stays the single source of truth for the
            // chosen track (the list box mirrors into it, all old logic unchanged)
            this.lblTrack.Location = new System.Drawing.Point(10, 8);
            this.lblTrack.Name = "lblTrack";
            this.lblTrack.Size = new System.Drawing.Size(10, 10);
            this.lblTrack.TabIndex = 2;
            this.lblTrack.Visible = false;
            this.cboTrack.DropDownStyle = System.Windows.Forms.ComboBoxStyle.DropDownList;
            this.cboTrack.Font = new System.Drawing.Font("Tahoma", 11.25F);
            this.cboTrack.Location = new System.Drawing.Point(24, 8);
            this.cboTrack.Name = "cboTrack";
            this.cboTrack.Size = new System.Drawing.Size(10, 34);
            this.cboTrack.TabIndex = 3;
            this.cboTrack.Visible = false;
            //
            // step 3: the three mode buttons carry generated icon images (set at load)
            this.btnMode1.Appearance = System.Windows.Forms.Appearance.Button;
            this.btnMode1.BackColor = System.Drawing.Color.AliceBlue;
            this.btnMode1.FlatAppearance.CheckedBackColor = System.Drawing.Color.Gold;
            this.btnMode1.FlatStyle = System.Windows.Forms.FlatStyle.Flat;
            this.btnMode1.Font = new System.Drawing.Font("Tahoma", 9F, System.Drawing.FontStyle.Bold);
            this.btnMode1.Location = new System.Drawing.Point(10, 64);
            this.btnMode1.Name = "btnMode1";
            this.btnMode1.Size = new System.Drawing.Size(104, 138);
            this.btnMode1.TabIndex = 4;
            this.btnMode1.Text = "Row by row";
            this.btnMode1.TextAlign = System.Drawing.ContentAlignment.BottomCenter;
            this.btnMode1.UseVisualStyleBackColor = false;
            this.btnMode1.CheckedChanged += new System.EventHandler(this.btnMode1_CheckedChanged);
            this.btnMode2.Appearance = System.Windows.Forms.Appearance.Button;
            this.btnMode2.BackColor = System.Drawing.Color.AliceBlue;
            this.btnMode2.FlatAppearance.CheckedBackColor = System.Drawing.Color.Gold;
            this.btnMode2.FlatStyle = System.Windows.Forms.FlatStyle.Flat;
            this.btnMode2.Font = new System.Drawing.Font("Tahoma", 9F, System.Drawing.FontStyle.Bold);
            this.btnMode2.Location = new System.Drawing.Point(122, 64);
            this.btnMode2.Name = "btnMode2";
            this.btnMode2.Size = new System.Drawing.Size(104, 138);
            this.btnMode2.TabIndex = 5;
            this.btnMode2.Text = "Contour";
            this.btnMode2.TextAlign = System.Drawing.ContentAlignment.BottomCenter;
            this.btnMode2.UseVisualStyleBackColor = false;
            this.btnMode2.CheckedChanged += new System.EventHandler(this.btnMode2_CheckedChanged);
            this.btnMode3.Appearance = System.Windows.Forms.Appearance.Button;
            this.btnMode3.BackColor = System.Drawing.Color.AliceBlue;
            this.btnMode3.Checked = true;
            this.btnMode3.CheckState = System.Windows.Forms.CheckState.Checked;
            this.btnMode3.FlatAppearance.CheckedBackColor = System.Drawing.Color.Gold;
            this.btnMode3.FlatStyle = System.Windows.Forms.FlatStyle.Flat;
            this.btnMode3.Font = new System.Drawing.Font("Tahoma", 9F, System.Drawing.FontStyle.Bold);
            this.btnMode3.Location = new System.Drawing.Point(232, 64);
            this.btnMode3.Name = "btnMode3";
            this.btnMode3.Size = new System.Drawing.Size(104, 138);
            this.btnMode3.TabIndex = 6;
            this.btnMode3.Text = "Zones (skip)";
            this.btnMode3.TextAlign = System.Drawing.ContentAlignment.BottomCenter;
            this.btnMode3.UseVisualStyleBackColor = false;
            this.btnMode3.CheckedChanged += new System.EventHandler(this.btnMode3_CheckedChanged);
            //
            // step 2: headland clip + zone count
            this.chkHeadlandClip.Appearance = System.Windows.Forms.Appearance.Button;
            this.chkHeadlandClip.BackColor = System.Drawing.Color.AliceBlue;
            this.chkHeadlandClip.Checked = true;
            this.chkHeadlandClip.CheckState = System.Windows.Forms.CheckState.Checked;
            this.chkHeadlandClip.FlatAppearance.CheckedBackColor = System.Drawing.Color.MediumAquamarine;
            this.chkHeadlandClip.FlatStyle = System.Windows.Forms.FlatStyle.Flat;
            this.chkHeadlandClip.Font = new System.Drawing.Font("Tahoma", 10F, System.Drawing.FontStyle.Bold);
            this.chkHeadlandClip.Location = new System.Drawing.Point(10, 64);
            this.chkHeadlandClip.Name = "chkHeadlandClip";
            this.chkHeadlandClip.Size = new System.Drawing.Size(326, 42);
            this.chkHeadlandClip.TabIndex = 7;
            this.chkHeadlandClip.Text = "Inside headland";
            this.chkHeadlandClip.TextAlign = System.Drawing.ContentAlignment.MiddleCenter;
            this.chkHeadlandClip.UseVisualStyleBackColor = false;
            //
            this.lblZones.Font = new System.Drawing.Font("Tahoma", 11.25F, System.Drawing.FontStyle.Bold);
            this.lblZones.Location = new System.Drawing.Point(10, 122);
            this.lblZones.Name = "lblZones";
            this.lblZones.Size = new System.Drawing.Size(104, 30);
            this.lblZones.TabIndex = 8;
            this.lblZones.Text = "Zones";
            this.lblZones.TextAlign = System.Drawing.ContentAlignment.MiddleLeft;
            this.nudZones.Font = new System.Drawing.Font("Tahoma", 12F);
            this.nudZones.Location = new System.Drawing.Point(120, 118);
            this.nudZones.Maximum = new decimal(new int[] { 9, 0, 0, 0 });
            this.nudZones.Minimum = new decimal(new int[] { 1, 0, 0, 0 });
            this.nudZones.Name = "nudZones";
            this.nudZones.Size = new System.Drawing.Size(84, 34);
            this.nudZones.TabIndex = 9;
            this.nudZones.Value = new decimal(new int[] { 2, 0, 0, 0 });
            this.nudZones.ValueChanged += new System.EventHandler(this.nudZones_ValueChanged);
            //
            // zone processing order (field feedback 2026-07-28): the order is edited by
            // tapping the zone badges on the map, this label only reports the layout
            //
            //
            this.lblZoneOrderRead.Font = new System.Drawing.Font("Tahoma", 9.75F);
            this.lblZoneOrderRead.ForeColor = System.Drawing.Color.DimGray;
            this.lblZoneOrderRead.Location = new System.Drawing.Point(10, 266);
            this.lblZoneOrderRead.Name = "lblZoneOrderRead";
            this.lblZoneOrderRead.Size = new System.Drawing.Size(300, 26);
            this.lblZoneOrderRead.TabIndex = 11;
            this.lblZoneOrderRead.TextAlign = System.Drawing.ContentAlignment.MiddleLeft;
            //
            this.lblPlanInfo.Font = new System.Drawing.Font("Tahoma", 9.75F);
            this.lblPlanInfo.ForeColor = System.Drawing.Color.DimGray;
            this.lblPlanInfo.Location = new System.Drawing.Point(10, 170);
            this.lblPlanInfo.Name = "lblPlanInfo";
            this.lblPlanInfo.Size = new System.Drawing.Size(326, 88);
            this.lblPlanInfo.TabIndex = 10;
            //
            this.btnGeneratePlan.BackColor = System.Drawing.Color.PapayaWhip;
            this.btnGeneratePlan.FlatStyle = System.Windows.Forms.FlatStyle.Flat;
            this.btnGeneratePlan.Font = new System.Drawing.Font("Tahoma", 11.25F, System.Drawing.FontStyle.Bold);
            this.btnGeneratePlan.Location = new System.Drawing.Point(10, 64);
            this.btnGeneratePlan.Name = "btnGeneratePlan";
            this.btnGeneratePlan.Size = new System.Drawing.Size(326, 48);
            this.btnGeneratePlan.TabIndex = 11;
            this.btnGeneratePlan.Text = "Generate plan";
            this.btnGeneratePlan.UseVisualStyleBackColor = false;
            this.btnGeneratePlan.Click += new System.EventHandler(this.btnGeneratePlan_Click);
            //
            // step 3: manual closing passes beside the list-selected line
            this.btnAddPath.BackColor = System.Drawing.Color.PapayaWhip;
            this.btnAddPath.FlatStyle = System.Windows.Forms.FlatStyle.Flat;
            this.btnAddPath.Font = new System.Drawing.Font("Tahoma", 10F, System.Drawing.FontStyle.Bold);
            this.btnAddPath.Location = new System.Drawing.Point(10, 404);
            this.btnAddPath.Name = "btnAddPath";
            this.btnAddPath.Size = new System.Drawing.Size(158, 42);
            this.btnAddPath.TabIndex = 12;
            this.btnAddPath.Text = "Add path";
            this.btnAddPath.UseVisualStyleBackColor = false;
            this.btnAddPath.Click += new System.EventHandler(this.btnAddPath_Click);
            this.btnDeletePath.BackColor = System.Drawing.Color.MistyRose;
            this.btnDeletePath.FlatStyle = System.Windows.Forms.FlatStyle.Flat;
            this.btnDeletePath.Font = new System.Drawing.Font("Tahoma", 10F, System.Drawing.FontStyle.Bold);
            this.btnDeletePath.Location = new System.Drawing.Point(178, 404);
            this.btnDeletePath.Name = "btnDeletePath";
            this.btnDeletePath.Size = new System.Drawing.Size(158, 42);
            this.btnDeletePath.TabIndex = 13;
            this.btnDeletePath.Text = "Delete path";
            this.btnDeletePath.UseVisualStyleBackColor = false;
            this.btnDeletePath.Click += new System.EventHandler(this.btnDeletePath_Click);
            //
            this.lblPlanStatus.Font = new System.Drawing.Font("Tahoma", 9.75F, System.Drawing.FontStyle.Bold);
            this.lblPlanStatus.ForeColor = System.Drawing.Color.DimGray;
            this.lblPlanStatus.Location = new System.Drawing.Point(10, 450);
            this.lblPlanStatus.Name = "lblPlanStatus";
            this.lblPlanStatus.Size = new System.Drawing.Size(326, 44);
            this.lblPlanStatus.TabIndex = 14;
            //
            // wizard navigation
            this.btnWizBack.Anchor = ((System.Windows.Forms.AnchorStyles)((System.Windows.Forms.AnchorStyles.Bottom | System.Windows.Forms.AnchorStyles.Left)));
            this.btnWizBack.BackColor = System.Drawing.Color.Gainsboro;
            this.btnWizBack.FlatStyle = System.Windows.Forms.FlatStyle.Flat;
            this.btnWizBack.Font = new System.Drawing.Font("Tahoma", 10F, System.Drawing.FontStyle.Bold);
            this.btnWizBack.Location = new System.Drawing.Point(10, 504);
            this.btnWizBack.Name = "btnWizBack";
            this.btnWizBack.Size = new System.Drawing.Size(102, 42);
            this.btnWizBack.TabIndex = 15;
            this.btnWizBack.Text = "Back";
            this.btnWizBack.UseVisualStyleBackColor = false;
            this.btnWizBack.Click += new System.EventHandler(this.btnWizBack_Click);
            this.btnWizNext.Anchor = ((System.Windows.Forms.AnchorStyles)((System.Windows.Forms.AnchorStyles.Bottom | System.Windows.Forms.AnchorStyles.Left)));
            this.btnWizNext.BackColor = System.Drawing.Color.MediumSeaGreen;
            this.btnWizNext.FlatStyle = System.Windows.Forms.FlatStyle.Flat;
            this.btnWizNext.Font = new System.Drawing.Font("Tahoma", 10F, System.Drawing.FontStyle.Bold);
            this.btnWizNext.Location = new System.Drawing.Point(122, 504);
            this.btnWizNext.Name = "btnWizNext";
            this.btnWizNext.Size = new System.Drawing.Size(102, 42);
            this.btnWizNext.TabIndex = 16;
            this.btnWizNext.Text = "Next";
            this.btnWizNext.UseVisualStyleBackColor = false;
            this.btnWizNext.Click += new System.EventHandler(this.btnWizNext_Click);
            this.btnWizCancel.Anchor = ((System.Windows.Forms.AnchorStyles)((System.Windows.Forms.AnchorStyles.Bottom | System.Windows.Forms.AnchorStyles.Left)));
            this.btnWizCancel.BackColor = System.Drawing.Color.Gainsboro;
            this.btnWizCancel.FlatStyle = System.Windows.Forms.FlatStyle.Flat;
            this.btnWizCancel.Font = new System.Drawing.Font("Tahoma", 10F, System.Drawing.FontStyle.Bold);
            this.btnWizCancel.Location = new System.Drawing.Point(234, 504);
            this.btnWizCancel.Name = "btnWizCancel";
            this.btnWizCancel.Size = new System.Drawing.Size(102, 42);
            this.btnWizCancel.TabIndex = 17;
            this.btnWizCancel.Text = "Cancel";
            this.btnWizCancel.UseVisualStyleBackColor = false;
            this.btnWizCancel.Click += new System.EventHandler(this.btnWizCancel_Click);
            //
            // ── SETTINGS view content (styles + per-style panels, unchanged) ──
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
            this.pnlK.Location = new System.Drawing.Point(16, 110);
            this.pnlK.Name = "pnlK";
            this.pnlK.Size = new System.Drawing.Size(984, 432);
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
            this.pnlU.Location = new System.Drawing.Point(16, 110);
            this.pnlU.Name = "pnlU";
            this.pnlU.Size = new System.Drawing.Size(984, 432);
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
            this.pnlRS.Location = new System.Drawing.Point(16, 110);
            this.pnlRS.Name = "pnlRS";
            this.pnlRS.Size = new System.Drawing.Size(984, 432);
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
            // trigger tolerance (common) - split before/after the turn start
            //
            this.lblTolerance.Font = new System.Drawing.Font("Tahoma", 11.25F, System.Drawing.FontStyle.Bold);
            this.lblTolerance.Location = new System.Drawing.Point(586, 80);
            this.lblTolerance.Name = "lblTolerance";
            this.lblTolerance.Size = new System.Drawing.Size(206, 30);
            this.lblTolerance.TabIndex = 6;
            this.lblTolerance.Text = "Trigger tol. before (m)";
            this.lblTolerance.TextAlign = System.Drawing.ContentAlignment.MiddleRight;
            this.nudTolerance.DecimalPlaces = 1;
            this.nudTolerance.Font = new System.Drawing.Font("Tahoma", 12F);
            this.nudTolerance.Increment = new decimal(new int[] { 2, 0, 0, 65536 });
            this.nudTolerance.Location = new System.Drawing.Point(802, 76);
            this.nudTolerance.Maximum = new decimal(new int[] { 10, 0, 0, 0 });
            this.nudTolerance.Minimum = new decimal(new int[] { 2, 0, 0, 65536 });
            this.nudTolerance.Name = "nudTolerance";
            this.nudTolerance.Size = new System.Drawing.Size(110, 34);
            this.nudTolerance.TabIndex = 7;
            //
            this.btnApplyParams.BackColor = System.Drawing.Color.Gainsboro;
            this.btnApplyParams.FlatStyle = System.Windows.Forms.FlatStyle.Flat;
            this.btnApplyParams.Font = new System.Drawing.Font("Tahoma", 11.25F, System.Drawing.FontStyle.Bold);
            this.btnApplyParams.Location = new System.Drawing.Point(803, 116);
            this.btnApplyParams.Name = "btnApplyParams";
            this.btnApplyParams.Size = new System.Drawing.Size(75, 44);
            this.btnApplyParams.TabIndex = 32;
            this.btnApplyParams.Text = "Apply";
            this.btnApplyParams.UseVisualStyleBackColor = false;
            this.btnApplyParams.Click += new System.EventHandler(this.btnApplyParams_Click);
            //
            this.lblBndOffset.Font = new System.Drawing.Font("Tahoma", 11.25F, System.Drawing.FontStyle.Bold);
            this.lblBndOffset.Location = new System.Drawing.Point(556, 8);
            this.lblBndOffset.Name = "lblBndOffset";
            this.lblBndOffset.Size = new System.Drawing.Size(236, 30);
            this.lblBndOffset.TabIndex = 30;
            this.lblBndOffset.Text = "Extra boundary offset (m)";
            this.lblBndOffset.TextAlign = System.Drawing.ContentAlignment.MiddleRight;
            //
            this.nudBndOffset.DecimalPlaces = 1;
            this.nudBndOffset.Font = new System.Drawing.Font("Tahoma", 12F);
            this.nudBndOffset.Increment = new decimal(new int[] { 1, 0, 0, 65536 });
            this.nudBndOffset.Location = new System.Drawing.Point(802, 4);
            this.nudBndOffset.Maximum = new decimal(new int[] { 20, 0, 0, 0 });
            this.nudBndOffset.Name = "nudBndOffset";
            this.nudBndOffset.Size = new System.Drawing.Size(110, 34);
            this.nudBndOffset.TabIndex = 31;
            this.nudBndOffset.Value = new decimal(new int[] { 5, 0, 0, 65536 });
            //
            this.lblToleranceAfter.Font = new System.Drawing.Font("Tahoma", 11.25F, System.Drawing.FontStyle.Bold);
            this.lblToleranceAfter.Location = new System.Drawing.Point(586, 44);
            this.lblToleranceAfter.Name = "lblToleranceAfter";
            this.lblToleranceAfter.Size = new System.Drawing.Size(206, 30);
            this.lblToleranceAfter.TabIndex = 8;
            this.lblToleranceAfter.Text = "Trigger tol. after (m)";
            this.lblToleranceAfter.TextAlign = System.Drawing.ContentAlignment.MiddleRight;
            this.nudToleranceAfter.DecimalPlaces = 1;
            this.nudToleranceAfter.Font = new System.Drawing.Font("Tahoma", 12F);
            this.nudToleranceAfter.Increment = new decimal(new int[] { 2, 0, 0, 65536 });
            this.nudToleranceAfter.Location = new System.Drawing.Point(802, 40);
            this.nudToleranceAfter.Maximum = new decimal(new int[] { 10, 0, 0, 0 });
            this.nudToleranceAfter.Minimum = new decimal(new int[] { 2, 0, 0, 65536 });
            this.nudToleranceAfter.Name = "nudToleranceAfter";
            this.nudToleranceAfter.Size = new System.Drawing.Size(110, 34);
            this.nudToleranceAfter.TabIndex = 9;
            //
            this.lblOvershootU.Font = new System.Drawing.Font("Tahoma", 11.25F, System.Drawing.FontStyle.Bold);
            this.lblOvershootU.Location = new System.Drawing.Point(4, 124);
            this.lblOvershootU.Name = "lblOvershootU";
            this.lblOvershootU.Size = new System.Drawing.Size(320, 30);
            this.lblOvershootU.TabIndex = 5;
            this.lblOvershootU.Text = "Overshoot distance (m)";
            this.lblOvershootU.TextAlign = System.Drawing.ContentAlignment.MiddleLeft;
            this.nudOvershootU.DecimalPlaces = 1;
            this.nudOvershootU.Font = new System.Drawing.Font("Tahoma", 12F);
            this.nudOvershootU.Increment = new decimal(new int[] { 5, 0, 0, 65536 });
            this.nudOvershootU.Location = new System.Drawing.Point(330, 120);
            this.nudOvershootU.Maximum = new decimal(new int[] { 10, 0, 0, 0 });
            this.nudOvershootU.Name = "nudOvershootU";
            this.nudOvershootU.Size = new System.Drawing.Size(130, 34);
            this.nudOvershootU.TabIndex = 6;
            ((System.ComponentModel.ISupportInitialize)(this.nudExitK)).BeginInit();
            ((System.ComponentModel.ISupportInitialize)(this.nudDistK)).BeginInit();
            ((System.ComponentModel.ISupportInitialize)(this.nudAngleK)).BeginInit();
            ((System.ComponentModel.ISupportInitialize)(this.nudTransK)).BeginInit();
            ((System.ComponentModel.ISupportInitialize)(this.nudOutU)).BeginInit();
            ((System.ComponentModel.ISupportInitialize)(this.nudInU)).BeginInit();
            ((System.ComponentModel.ISupportInitialize)(this.nudMinRS)).BeginInit();
            ((System.ComponentModel.ISupportInitialize)(this.nudBndOffset)).BeginInit();
            ((System.ComponentModel.ISupportInitialize)(this.nudTolerance)).BeginInit();
            ((System.ComponentModel.ISupportInitialize)(this.nudToleranceAfter)).BeginInit();
            ((System.ComponentModel.ISupportInitialize)(this.nudOvershootU)).BeginInit();
            ((System.ComponentModel.ISupportInitialize)(this.nudZones)).BeginInit();
            //
            // FormTurnLine
            //
            this.AutoScaleDimensions = new System.Drawing.SizeF(6F, 13F);
            this.AutoScaleMode = System.Windows.Forms.AutoScaleMode.Font;
            this.BackColor = System.Drawing.Color.WhiteSmoke;
            this.ClientSize = new System.Drawing.Size(1040, 684);
            this.Controls.Add(this.pnlMapTools);
            this.Controls.Add(this.pnlPath);
            this.Controls.Add(this.pnlParams);
            this.Controls.Add(this.btnTabPath);
            this.Controls.Add(this.btnTabParams);
            this.Controls.Add(this.btnTabMap);
            this.Controls.Add(this.btnResetPath);
            this.Controls.Add(this.btnWinFull);
            this.Controls.Add(this.btnWinClose);
            this.Controls.Add(this.btnZoomIn);
            this.Controls.Add(this.btnZoomOut);
            this.Controls.Add(this.lblStatus);
            this.Controls.Add(this.btnSave);
            this.Controls.Add(this.lblRadius);
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
            ((System.ComponentModel.ISupportInitialize)(this.nudBndOffset)).EndInit();
            ((System.ComponentModel.ISupportInitialize)(this.nudTolerance)).EndInit();
            ((System.ComponentModel.ISupportInitialize)(this.nudToleranceAfter)).EndInit();
            ((System.ComponentModel.ISupportInitialize)(this.nudOvershootU)).EndInit();
            ((System.ComponentModel.ISupportInitialize)(this.nudZones)).EndInit();
            this.ResumeLayout(false);
        }

        #endregion

        private System.Windows.Forms.CheckBox chkActivate;
        private System.Windows.Forms.CheckBox chkAutomatic;
        private System.Windows.Forms.Button btnSave;
        private System.Windows.Forms.Label lblRadius;
        private System.Windows.Forms.Button btnTabPath;
        private System.Windows.Forms.Button btnTabParams;
        private System.Windows.Forms.Button btnTabMap;
        private System.Windows.Forms.Button btnResetPath;
        private System.Windows.Forms.Label lblStatus;
        private GMap.NET.WindowsForms.GMapControl gMapControl;
        private System.Windows.Forms.Button btnZoomIn;
        private System.Windows.Forms.Button btnZoomOut;
        private System.Windows.Forms.Button btnMeasure;
        private System.Windows.Forms.Label lblMeasure;
        private System.Windows.Forms.Button btnWinFull;
        private System.Windows.Forms.Button btnWinClose;
        private System.Windows.Forms.Panel pnlMapTools;
        private System.Windows.Forms.Button btnSaveLines;
        private System.Windows.Forms.Button btnAddLine;
        private System.Windows.Forms.CheckBox btnBorderLine;
        private System.Windows.Forms.CheckBox btnAddPoint;
        private System.Windows.Forms.CheckBox btnDeletePoint;
        private System.Windows.Forms.CheckBox btnDeleteLine;
        private System.Windows.Forms.CheckBox chkParcels;
        private System.Windows.Forms.Button btnGoToField;
        private System.Windows.Forms.Panel pnlParams;
        private System.Windows.Forms.Panel pnlPath;
        private System.Windows.Forms.Label lblWizTitle;
        private System.Windows.Forms.ListBox lstLines;
        private System.Windows.Forms.Label lblTrack;
        private System.Windows.Forms.ComboBox cboTrack;
        private System.Windows.Forms.CheckBox btnMode1;
        private System.Windows.Forms.CheckBox btnMode2;
        private System.Windows.Forms.CheckBox btnMode3;
        private System.Windows.Forms.Label lblZones;
        private System.Windows.Forms.NumericUpDown nudZones;
        private System.Windows.Forms.Label lblZoneOrderRead;
        private System.Windows.Forms.CheckBox chkHeadlandClip;
        private System.Windows.Forms.Label lblPlanInfo;
        private System.Windows.Forms.Button btnGeneratePlan;
        private System.Windows.Forms.Button btnAddPath;
        private System.Windows.Forms.Button btnDeletePath;
        private System.Windows.Forms.Label lblPlanStatus;
        private System.Windows.Forms.Button btnWizBack;
        private System.Windows.Forms.Button btnWizNext;
        private System.Windows.Forms.Button btnWizCancel;
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
        private System.Windows.Forms.Label lblBndOffset;
        private System.Windows.Forms.Button btnApplyParams;
        private AgOpenGPS.NudlessNumericUpDown nudBndOffset;
        private System.Windows.Forms.NumericUpDown nudTolerance;
        private System.Windows.Forms.Label lblToleranceAfter;
        private System.Windows.Forms.NumericUpDown nudToleranceAfter;
        private System.Windows.Forms.Label lblOvershootU;
        private System.Windows.Forms.NumericUpDown nudOvershootU;
    }
}
