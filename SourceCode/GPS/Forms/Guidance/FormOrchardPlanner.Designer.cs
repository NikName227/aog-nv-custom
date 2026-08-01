namespace AgOpenGPS
{
    partial class FormOrchardPlanner
    {
        private System.ComponentModel.IContainer components = null;

        protected override void Dispose(bool disposing)
        {
            if (disposing && (components != null)) components.Dispose();
            base.Dispose(disposing);
        }

        #region Windows Form Designer generated code

        private void InitializeComponent()
        {
            this.oglSelf = new OpenTK.GLControl();
            this.lblActiveOp = new System.Windows.Forms.Label();
            this.btnZoomIn = new System.Windows.Forms.Button();
            this.btnZoomOut = new System.Windows.Forms.Button();
            this.btnZoomRestore = new System.Windows.Forms.Button();
            this.btnSat = new System.Windows.Forms.CheckBox();
            this.btnWinFull = new System.Windows.Forms.Button();
            this.btnWinClose = new System.Windows.Forms.Button();
            this.lblName = new System.Windows.Forms.Label();
            this.tboxName = new System.Windows.Forms.TextBox();
            this.lblLegCap = new System.Windows.Forms.Label();
            this.lblLegRow = new System.Windows.Forms.Label();
            this.lblLegMid = new System.Windows.Forms.Label();
            this.lblLegTree = new System.Windows.Forms.Label();
            this.btnFine = new System.Windows.Forms.Button();
            this.btnCoarse = new System.Windows.Forms.Button();
            this.btnMeasure = new System.Windows.Forms.Button();
            this.lblMeasure = new System.Windows.Forms.Label();
            this.btnNavUp = new System.Windows.Forms.Button();
            this.btnNavDown = new System.Windows.Forms.Button();
            this.btnNavLeft = new System.Windows.Forms.Button();
            this.btnNavRight = new System.Windows.Forms.Button();
            this.btnPoint = new System.Windows.Forms.Button();
            this.lblPointMode = new System.Windows.Forms.Label();
            this.btnAddPoint = new System.Windows.Forms.CheckBox();
            this.btnLimitEdit = new System.Windows.Forms.CheckBox();
            this.btnDeletePoint = new System.Windows.Forms.CheckBox();
            this.btnImportKml = new System.Windows.Forms.Button();
            this.btnMakePath = new System.Windows.Forms.Button();
            this.lblWarn = new System.Windows.Forms.Label();
            this.btnNewRow = new System.Windows.Forms.Button();
            this.btnEdit = new System.Windows.Forms.Button();
            this.btnPrevRow = new System.Windows.Forms.Button();
            this.btnNextRow = new System.Windows.Forms.Button();
            this.btnDeleteRow = new System.Windows.Forms.Button();
            this.btnCancel = new System.Windows.Forms.Button();
            this.btnAccept = new System.Windows.Forms.Button();
            this.btnNewOrchard = new System.Windows.Forms.Button();
            this.btnTreesOnRow = new System.Windows.Forms.Button();
            this.lblFilterCap = new System.Windows.Forms.Label();
            this.lblThin = new System.Windows.Forms.Label();
            this.btnThinDown = new System.Windows.Forms.Button();
            this.btnThinUp = new System.Windows.Forms.Button();
            this.lblThinVal = new System.Windows.Forms.Label();
            this.lblSmooth = new System.Windows.Forms.Label();
            this.btnSmoothDown = new System.Windows.Forms.Button();
            this.btnSmoothUp = new System.Windows.Forms.Button();
            this.lblSmoothVal = new System.Windows.Forms.Label();
            this.btnFilterApply = new System.Windows.Forms.Button();
            this.pnlGen = new System.Windows.Forms.Panel();
            this.lblGenCap = new System.Windows.Forms.Label();
            this.lblGenRowSpace = new System.Windows.Forms.Label();
            this.nudGenRowSpace = new AgOpenGPS.NudlessNumericUpDown();
            this.lblGenTreeSpace = new System.Windows.Forms.Label();
            this.nudGenTreeSpace = new AgOpenGPS.NudlessNumericUpDown();
            this.lblGenHeadland = new System.Windows.Forms.Label();
            this.nudGenHeadland = new AgOpenGPS.NudlessNumericUpDown();
            this.lblGenSetback = new System.Windows.Forms.Label();
            this.nudGenSetback = new AgOpenGPS.NudlessNumericUpDown();
            this.chkGenStagger = new System.Windows.Forms.CheckBox();
            this.btnGenLine = new System.Windows.Forms.Button();
            this.btnGenerate = new System.Windows.Forms.Button();
            this.lblGenResult = new System.Windows.Forms.Label();
            this.btnGenClose = new System.Windows.Forms.Button();
            this.nudRefLong = new AgOpenGPS.NudlessNumericUpDown();
            this.btnRefDir = new System.Windows.Forms.Button();
            this.btnMarkTree = new System.Windows.Forms.Button();
            this.btnEditTrees = new System.Windows.Forms.Button();
            this.btnDeleteLastTree = new System.Windows.Forms.Button();
            this.lblTreeMinDist = new System.Windows.Forms.Label();
            this.nudTreeMinDist = new AgOpenGPS.NudlessNumericUpDown();
            this.btnKeyTrigger = new System.Windows.Forms.Button();
            this.lblKeyTrigger = new System.Windows.Forms.Label();
            this.SuspendLayout();
            //
            // oglSelf
            //
            this.oglSelf.BackColor = System.Drawing.Color.Black;
            this.oglSelf.Cursor = System.Windows.Forms.Cursors.Cross;
            this.oglSelf.Location = new System.Drawing.Point(8, 8);
            this.oglSelf.Margin = new System.Windows.Forms.Padding(0);
            this.oglSelf.Name = "oglSelf";
            this.oglSelf.Size = new System.Drawing.Size(660, 660);
            this.oglSelf.TabIndex = 0;
            this.oglSelf.VSync = false;
            this.oglSelf.Load += new System.EventHandler(this.oglSelf_Load);
            this.oglSelf.Paint += new System.Windows.Forms.PaintEventHandler(this.oglSelf_Paint);
            this.oglSelf.MouseDown += new System.Windows.Forms.MouseEventHandler(this.oglSelf_MouseDown);
            this.oglSelf.MouseMove += new System.Windows.Forms.MouseEventHandler(this.oglSelf_MouseMove);
            this.oglSelf.MouseUp += new System.Windows.Forms.MouseEventHandler(this.oglSelf_MouseUp);
            this.oglSelf.Resize += new System.EventHandler(this.oglSelf_Resize);
            //
            // lblActiveOp
            //
            this.lblActiveOp.AutoSize = true;
            this.lblActiveOp.Font = new System.Drawing.Font("Tahoma", 11.25F, System.Drawing.FontStyle.Bold);
            this.lblActiveOp.ForeColor = System.Drawing.Color.DimGray;
            this.lblActiveOp.Location = new System.Drawing.Point(8, 674);
            this.lblActiveOp.Name = "lblActiveOp";
            this.lblActiveOp.Size = new System.Drawing.Size(0, 18);
            this.lblActiveOp.TabIndex = 1;
            //
            // btnZoomIn
            //
            this.btnZoomIn.BackColor = System.Drawing.Color.White;
            this.btnZoomIn.Font = new System.Drawing.Font("Tahoma", 20F, System.Drawing.FontStyle.Bold);
            this.btnZoomIn.Location = new System.Drawing.Point(604, 544);
            this.btnZoomIn.Name = "btnZoomIn";
            this.btnZoomIn.Size = new System.Drawing.Size(56, 56);
            this.btnZoomIn.TabIndex = 2;
            this.btnZoomIn.Text = "+";
            this.btnZoomIn.UseVisualStyleBackColor = false;
            this.btnZoomIn.Click += new System.EventHandler(this.btnZoomIn_Click);
            //
            // btnZoomOut
            //
            this.btnZoomOut.BackColor = System.Drawing.Color.White;
            this.btnZoomOut.Font = new System.Drawing.Font("Tahoma", 20F, System.Drawing.FontStyle.Bold);
            this.btnZoomOut.Location = new System.Drawing.Point(604, 606);
            this.btnZoomOut.Name = "btnZoomOut";
            this.btnZoomOut.Size = new System.Drawing.Size(56, 56);
            this.btnZoomOut.TabIndex = 3;
            this.btnZoomOut.Text = "−";
            this.btnZoomOut.UseVisualStyleBackColor = false;
            this.btnZoomOut.Click += new System.EventHandler(this.btnZoomOut_Click);
            //
            // btnZoomRestore
            //
            this.btnZoomRestore.BackColor = System.Drawing.Color.White;
            this.btnZoomRestore.Font = new System.Drawing.Font("Tahoma", 16F, System.Drawing.FontStyle.Bold);
            this.btnZoomRestore.Location = new System.Drawing.Point(604, 575);
            this.btnZoomRestore.Name = "btnZoomRestore";
            this.btnZoomRestore.Size = new System.Drawing.Size(56, 56);
            this.btnZoomRestore.TabIndex = 4;
            this.btnZoomRestore.Text = "○";
            this.btnZoomRestore.UseVisualStyleBackColor = false;
            this.btnZoomRestore.Click += new System.EventHandler(this.btnZoomRestore_Click);
            //
            // btnSat
            //
            this.btnSat.Appearance = System.Windows.Forms.Appearance.Button;
            this.btnSat.BackColor = System.Drawing.Color.White;
            this.btnSat.FlatAppearance.CheckedBackColor = System.Drawing.Color.MediumAquamarine;
            this.btnSat.FlatStyle = System.Windows.Forms.FlatStyle.Flat;
            this.btnSat.Font = new System.Drawing.Font("Tahoma", 8F, System.Drawing.FontStyle.Bold);
            this.btnSat.Location = new System.Drawing.Point(604, 14);
            this.btnSat.Name = "btnSat";
            this.btnSat.Size = new System.Drawing.Size(56, 50);
            this.btnSat.TabIndex = 5;
            this.btnSat.Text = "SAT";
            this.btnSat.TextAlign = System.Drawing.ContentAlignment.MiddleCenter;
            this.btnSat.UseVisualStyleBackColor = false;
            this.btnSat.CheckedChanged += new System.EventHandler(this.btnSat_CheckedChanged);
            //
            // btnWinFull
            //
            this.btnWinFull.BackColor = System.Drawing.Color.Gainsboro;
            this.btnWinFull.FlatStyle = System.Windows.Forms.FlatStyle.Flat;
            this.btnWinFull.Font = new System.Drawing.Font("Tahoma", 14F, System.Drawing.FontStyle.Bold);
            this.btnWinFull.Location = new System.Drawing.Point(880, 6);
            this.btnWinFull.Name = "btnWinFull";
            this.btnWinFull.Size = new System.Drawing.Size(50, 38);
            this.btnWinFull.TabIndex = 31;
            this.btnWinFull.Text = "❐";
            this.btnWinFull.UseVisualStyleBackColor = false;
            this.btnWinFull.Click += new System.EventHandler(this.btnWinFull_Click);
            //
            // btnWinClose
            //
            this.btnWinClose.BackColor = System.Drawing.Color.MistyRose;
            this.btnWinClose.FlatStyle = System.Windows.Forms.FlatStyle.Flat;
            this.btnWinClose.Font = new System.Drawing.Font("Tahoma", 14F, System.Drawing.FontStyle.Bold);
            this.btnWinClose.Location = new System.Drawing.Point(938, 6);
            this.btnWinClose.Name = "btnWinClose";
            this.btnWinClose.Size = new System.Drawing.Size(50, 38);
            this.btnWinClose.TabIndex = 32;
            this.btnWinClose.Text = "✕";
            this.btnWinClose.UseVisualStyleBackColor = false;
            this.btnWinClose.Click += new System.EventHandler(this.btnWinClose_Click);
            //
            // lblName
            //
            this.lblName.AutoSize = true;
            this.lblName.Font = new System.Drawing.Font("Tahoma", 9.75F, System.Drawing.FontStyle.Bold);
            this.lblName.Location = new System.Drawing.Point(684, 616);
            this.lblName.Name = "lblName";
            this.lblName.Size = new System.Drawing.Size(43, 16);
            this.lblName.TabIndex = 33;
            this.lblName.Text = "Name";
            //
            // tboxName
            //
            this.tboxName.Font = new System.Drawing.Font("Tahoma", 11.25F);
            this.tboxName.Location = new System.Drawing.Point(740, 612);
            this.tboxName.Name = "tboxName";
            this.tboxName.Size = new System.Drawing.Size(246, 26);
            this.tboxName.TabIndex = 34;
            //
            // lblLegCap
            //
            this.lblLegCap.AutoSize = true;
            this.lblLegCap.Font = new System.Drawing.Font("Tahoma", 9F, System.Drawing.FontStyle.Bold);
            this.lblLegCap.ForeColor = System.Drawing.Color.DimGray;
            this.lblLegCap.Location = new System.Drawing.Point(684, 8);
            this.lblLegCap.Name = "lblLegCap";
            this.lblLegCap.Size = new System.Drawing.Size(50, 15);
            this.lblLegCap.TabIndex = 35;
            this.lblLegCap.Text = "Legend";
            //
            // lblLegRow
            //
            this.lblLegRow.BackColor = System.Drawing.Color.LimeGreen;
            this.lblLegRow.BorderStyle = System.Windows.Forms.BorderStyle.FixedSingle;
            this.lblLegRow.Font = new System.Drawing.Font("Tahoma", 8.25F, System.Drawing.FontStyle.Bold);
            this.lblLegRow.Location = new System.Drawing.Point(684, 26);
            this.lblLegRow.Name = "lblLegRow";
            this.lblLegRow.Size = new System.Drawing.Size(60, 24);
            this.lblLegRow.TabIndex = 36;
            this.lblLegRow.Text = "Row";
            this.lblLegRow.TextAlign = System.Drawing.ContentAlignment.MiddleCenter;
            //
            // lblLegMid
            //
            this.lblLegMid.BackColor = System.Drawing.Color.Gold;
            this.lblLegMid.BorderStyle = System.Windows.Forms.BorderStyle.FixedSingle;
            this.lblLegMid.Font = new System.Drawing.Font("Tahoma", 8.25F, System.Drawing.FontStyle.Bold);
            this.lblLegMid.Location = new System.Drawing.Point(748, 26);
            this.lblLegMid.Name = "lblLegMid";
            this.lblLegMid.Size = new System.Drawing.Size(70, 24);
            this.lblLegMid.TabIndex = 37;
            this.lblLegMid.Text = "Mid-line";
            this.lblLegMid.TextAlign = System.Drawing.ContentAlignment.MiddleCenter;
            //
            // lblLegTree
            //
            this.lblLegTree.BackColor = System.Drawing.Color.RosyBrown;
            this.lblLegTree.BorderStyle = System.Windows.Forms.BorderStyle.FixedSingle;
            this.lblLegTree.Font = new System.Drawing.Font("Tahoma", 8.25F, System.Drawing.FontStyle.Bold);
            this.lblLegTree.Location = new System.Drawing.Point(822, 26);
            this.lblLegTree.Name = "lblLegTree";
            this.lblLegTree.Size = new System.Drawing.Size(60, 24);
            this.lblLegTree.TabIndex = 38;
            this.lblLegTree.Text = "Tree";
            this.lblLegTree.TextAlign = System.Drawing.ContentAlignment.MiddleCenter;
            //
            // btnFine
            //
            this.btnFine.FlatStyle = System.Windows.Forms.FlatStyle.Flat;
            this.btnFine.Font = new System.Drawing.Font("Tahoma", 10F, System.Drawing.FontStyle.Bold);
            this.btnFine.Location = new System.Drawing.Point(684, 100);
            this.btnFine.Name = "btnFine";
            this.btnFine.Size = new System.Drawing.Size(80, 44);
            this.btnFine.TabIndex = 6;
            this.btnFine.Text = "Fine";
            this.btnFine.UseVisualStyleBackColor = true;
            this.btnFine.Click += new System.EventHandler(this.btnFine_Click);
            //
            // btnCoarse
            //
            this.btnCoarse.BackColor = System.Drawing.Color.LightSteelBlue;
            this.btnCoarse.FlatStyle = System.Windows.Forms.FlatStyle.Flat;
            this.btnCoarse.Font = new System.Drawing.Font("Tahoma", 10F, System.Drawing.FontStyle.Bold);
            this.btnCoarse.Location = new System.Drawing.Point(684, 150);
            this.btnCoarse.Name = "btnCoarse";
            this.btnCoarse.Size = new System.Drawing.Size(80, 44);
            this.btnCoarse.TabIndex = 7;
            this.btnCoarse.Text = "Coarse";
            this.btnCoarse.UseVisualStyleBackColor = false;
            this.btnCoarse.Click += new System.EventHandler(this.btnCoarse_Click);
            //
            // btnMeasure
            //
            this.btnMeasure.Font = new System.Drawing.Font("Tahoma", 9.75F, System.Drawing.FontStyle.Bold);
            this.btnMeasure.Location = new System.Drawing.Point(684, 212);
            this.btnMeasure.Name = "btnMeasure";
            this.btnMeasure.Size = new System.Drawing.Size(80, 44);
            this.btnMeasure.TabIndex = 8;
            this.btnMeasure.Text = "Measure";
            this.btnMeasure.UseVisualStyleBackColor = true;
            this.btnMeasure.Click += new System.EventHandler(this.btnMeasure_Click);
            //
            // lblMeasure
            //
            this.lblMeasure.Font = new System.Drawing.Font("Tahoma", 11F, System.Drawing.FontStyle.Bold);
            this.lblMeasure.ForeColor = System.Drawing.Color.Firebrick;
            this.lblMeasure.Location = new System.Drawing.Point(684, 260);
            this.lblMeasure.Name = "lblMeasure";
            this.lblMeasure.Size = new System.Drawing.Size(104, 22);
            this.lblMeasure.TabIndex = 9;
            this.lblMeasure.TextAlign = System.Drawing.ContentAlignment.MiddleCenter;
            //
            // btnNavUp
            //
            this.btnNavUp.FlatStyle = System.Windows.Forms.FlatStyle.Flat;
            this.btnNavUp.Font = new System.Drawing.Font("Tahoma", 20F, System.Drawing.FontStyle.Bold);
            this.btnNavUp.ForeColor = System.Drawing.Color.RoyalBlue;
            this.btnNavUp.Location = new System.Drawing.Point(854, 92);
            this.btnNavUp.Name = "btnNavUp";
            this.btnNavUp.Size = new System.Drawing.Size(60, 60);
            this.btnNavUp.TabIndex = 10;
            this.btnNavUp.Text = "▲";
            this.btnNavUp.UseVisualStyleBackColor = true;
            this.btnNavUp.Click += new System.EventHandler(this.btnNavUp_Click);
            //
            // btnNavLeft
            //
            this.btnNavLeft.FlatStyle = System.Windows.Forms.FlatStyle.Flat;
            this.btnNavLeft.Font = new System.Drawing.Font("Tahoma", 20F, System.Drawing.FontStyle.Bold);
            this.btnNavLeft.ForeColor = System.Drawing.Color.RoyalBlue;
            this.btnNavLeft.Location = new System.Drawing.Point(790, 156);
            this.btnNavLeft.Name = "btnNavLeft";
            this.btnNavLeft.Size = new System.Drawing.Size(60, 60);
            this.btnNavLeft.TabIndex = 11;
            this.btnNavLeft.Text = "◄";
            this.btnNavLeft.UseVisualStyleBackColor = true;
            this.btnNavLeft.Click += new System.EventHandler(this.btnNavLeft_Click);
            //
            // btnPoint
            //
            this.btnPoint.BackColor = System.Drawing.Color.Gainsboro;
            this.btnPoint.FlatStyle = System.Windows.Forms.FlatStyle.Flat;
            this.btnPoint.Font = new System.Drawing.Font("Tahoma", 18F, System.Drawing.FontStyle.Bold);
            this.btnPoint.Location = new System.Drawing.Point(854, 156);
            this.btnPoint.Name = "btnPoint";
            this.btnPoint.Size = new System.Drawing.Size(60, 60);
            this.btnPoint.TabIndex = 12;
            this.btnPoint.Text = "●";
            this.btnPoint.UseVisualStyleBackColor = false;
            this.btnPoint.Click += new System.EventHandler(this.btnPoint_Click);
            //
            // btnNavRight
            //
            this.btnNavRight.FlatStyle = System.Windows.Forms.FlatStyle.Flat;
            this.btnNavRight.Font = new System.Drawing.Font("Tahoma", 20F, System.Drawing.FontStyle.Bold);
            this.btnNavRight.ForeColor = System.Drawing.Color.RoyalBlue;
            this.btnNavRight.Location = new System.Drawing.Point(918, 156);
            this.btnNavRight.Name = "btnNavRight";
            this.btnNavRight.Size = new System.Drawing.Size(60, 60);
            this.btnNavRight.TabIndex = 13;
            this.btnNavRight.Text = "►";
            this.btnNavRight.UseVisualStyleBackColor = true;
            this.btnNavRight.Click += new System.EventHandler(this.btnNavRight_Click);
            //
            // btnNavDown
            //
            this.btnNavDown.FlatStyle = System.Windows.Forms.FlatStyle.Flat;
            this.btnNavDown.Font = new System.Drawing.Font("Tahoma", 20F, System.Drawing.FontStyle.Bold);
            this.btnNavDown.ForeColor = System.Drawing.Color.RoyalBlue;
            this.btnNavDown.Location = new System.Drawing.Point(854, 220);
            this.btnNavDown.Name = "btnNavDown";
            this.btnNavDown.Size = new System.Drawing.Size(60, 60);
            this.btnNavDown.TabIndex = 14;
            this.btnNavDown.Text = "▼";
            this.btnNavDown.UseVisualStyleBackColor = true;
            this.btnNavDown.Click += new System.EventHandler(this.btnNavDown_Click);
            //
            // lblPointMode
            //
            this.lblPointMode.Font = new System.Drawing.Font("Tahoma", 11.25F, System.Drawing.FontStyle.Bold);
            this.lblPointMode.ForeColor = System.Drawing.Color.DimGray;
            this.lblPointMode.Location = new System.Drawing.Point(786, 286);
            this.lblPointMode.Name = "lblPointMode";
            this.lblPointMode.Size = new System.Drawing.Size(200, 24);
            this.lblPointMode.TabIndex = 15;
            this.lblPointMode.TextAlign = System.Drawing.ContentAlignment.MiddleCenter;
            //
            // btnLimitEdit
            //
            //F50 (2026-08-01): opens the steer-limit polygon for editing. Sits directly above
            //"Add point" and is deliberately narrower and smaller - it is set once per field,
            //not a tool reached for on every pass.
            this.btnLimitEdit.Appearance = System.Windows.Forms.Appearance.Button;
            this.btnLimitEdit.AutoCheck = false;
            this.btnLimitEdit.BackColor = System.Drawing.Color.AliceBlue;
            this.btnLimitEdit.FlatAppearance.CheckedBackColor = System.Drawing.Color.Gold;
            this.btnLimitEdit.FlatStyle = System.Windows.Forms.FlatStyle.Flat;
            this.btnLimitEdit.Font = new System.Drawing.Font("Tahoma", 8.25F, System.Drawing.FontStyle.Bold);
            this.btnLimitEdit.Location = new System.Drawing.Point(684, 282);
            this.btnLimitEdit.Name = "btnLimitEdit";
            this.btnLimitEdit.Size = new System.Drawing.Size(110, 32);
            this.btnLimitEdit.TabStop = false;
            this.btnLimitEdit.Text = "Steer\r\nlimit";
            this.btnLimitEdit.TextAlign = System.Drawing.ContentAlignment.MiddleCenter;
            this.btnLimitEdit.UseVisualStyleBackColor = false;
            this.btnLimitEdit.Click += new System.EventHandler(this.btnLimitEdit_Click);
            //
            // btnAddPoint
            //
            this.btnAddPoint.Appearance = System.Windows.Forms.Appearance.Button;
            this.btnAddPoint.AutoCheck = false;
            this.btnAddPoint.BackColor = System.Drawing.Color.AliceBlue;
            this.btnAddPoint.FlatAppearance.CheckedBackColor = System.Drawing.Color.Gold;
            this.btnAddPoint.FlatStyle = System.Windows.Forms.FlatStyle.Flat;
            this.btnAddPoint.Font = new System.Drawing.Font("Tahoma", 10F, System.Drawing.FontStyle.Bold);
            this.btnAddPoint.Location = new System.Drawing.Point(684, 316);
            this.btnAddPoint.Name = "btnAddPoint";
            this.btnAddPoint.Size = new System.Drawing.Size(145, 44);
            this.btnAddPoint.TabIndex = 16;
            this.btnAddPoint.Text = "Add point";
            this.btnAddPoint.TextAlign = System.Drawing.ContentAlignment.MiddleCenter;
            this.btnAddPoint.UseVisualStyleBackColor = false;
            this.btnAddPoint.Click += new System.EventHandler(this.btnAddPoint_Click);
            //
            // btnDeletePoint
            //
            this.btnDeletePoint.Appearance = System.Windows.Forms.Appearance.Button;
            this.btnDeletePoint.BackColor = System.Drawing.Color.AliceBlue;
            this.btnDeletePoint.FlatAppearance.CheckedBackColor = System.Drawing.Color.IndianRed;
            this.btnDeletePoint.FlatStyle = System.Windows.Forms.FlatStyle.Flat;
            this.btnDeletePoint.Font = new System.Drawing.Font("Tahoma", 10F, System.Drawing.FontStyle.Bold);
            this.btnDeletePoint.Location = new System.Drawing.Point(835, 316);
            this.btnDeletePoint.Name = "btnDeletePoint";
            this.btnDeletePoint.Size = new System.Drawing.Size(145, 44);
            this.btnDeletePoint.TabIndex = 17;
            this.btnDeletePoint.Text = "Delete point";
            this.btnDeletePoint.TextAlign = System.Drawing.ContentAlignment.MiddleCenter;
            this.btnDeletePoint.UseVisualStyleBackColor = false;
            this.btnDeletePoint.CheckedChanged += new System.EventHandler(this.btnDeletePoint_CheckedChanged);
            //
            // btnImportKml
            //
            this.btnImportKml.BackColor = System.Drawing.Color.AliceBlue;
            this.btnImportKml.FlatStyle = System.Windows.Forms.FlatStyle.Flat;
            this.btnImportKml.Font = new System.Drawing.Font("Tahoma", 9.75F, System.Drawing.FontStyle.Bold);
            this.btnImportKml.Location = new System.Drawing.Point(684, 366);
            this.btnImportKml.Name = "btnImportKml";
            this.btnImportKml.Size = new System.Drawing.Size(145, 44);
            this.btnImportKml.TabIndex = 18;
            this.btnImportKml.Text = "Import KML";
            this.btnImportKml.UseVisualStyleBackColor = false;
            this.btnImportKml.Click += new System.EventHandler(this.btnImportKml_Click);
            //
            // btnMakePath
            //
            this.btnMakePath.BackColor = System.Drawing.Color.LightGoldenrodYellow;
            this.btnMakePath.Enabled = false;
            this.btnMakePath.FlatStyle = System.Windows.Forms.FlatStyle.Flat;
            this.btnMakePath.Font = new System.Drawing.Font("Tahoma", 9.75F, System.Drawing.FontStyle.Bold);
            this.btnMakePath.Location = new System.Drawing.Point(835, 366);
            this.btnMakePath.Name = "btnMakePath";
            this.btnMakePath.Size = new System.Drawing.Size(145, 44);
            this.btnMakePath.TabIndex = 19;
            this.btnMakePath.Text = "Make path";
            this.btnMakePath.UseVisualStyleBackColor = false;
            //SIM 0729 t4: this window only DEFINES rows - the driving path comes from the
            //rows plus the implement settings and is (re)generated automatically, so the
            //manual trigger is hidden. BuildPasses/btnMakePath_Click stay as the engine.
            this.btnMakePath.Visible = false;
            this.btnMakePath.Click += new System.EventHandler(this.btnMakePath_Click);
            //
            // lblWarn
            //
            this.lblWarn.AutoSize = false;
            this.lblWarn.Font = new System.Drawing.Font("Tahoma", 9.75F, System.Drawing.FontStyle.Bold);
            this.lblWarn.ForeColor = System.Drawing.Color.Firebrick;
            this.lblWarn.Location = new System.Drawing.Point(684, 471);
            this.lblWarn.Name = "lblWarn";
            this.lblWarn.Size = new System.Drawing.Size(145, 18);
            this.lblWarn.TabIndex = 20;
            //
            // btnRecordRow (F50 phase 2: record a row by DRIVING it, boundary-recorder
            // style - the point lands offset from the pivot so the tractor can keep a
            // safe distance from the tree line / slope)
            //
            this.btnRecordRow = new System.Windows.Forms.Button();
            this.nudRecOffset = new AgOpenGPS.NudlessNumericUpDown();
            this.btnRecSide = new System.Windows.Forms.Button();
            this.btnRecordRow.BackColor = System.Drawing.Color.MistyRose;
            this.btnRecordRow.FlatStyle = System.Windows.Forms.FlatStyle.Flat;
            this.btnRecordRow.Font = new System.Drawing.Font("Tahoma", 11.25F, System.Drawing.FontStyle.Bold);
            this.btnRecordRow.Location = new System.Drawing.Point(684, 490);
            this.btnRecordRow.Name = "btnRecordRow";
            this.btnRecordRow.Size = new System.Drawing.Size(145, 44);
            this.btnRecordRow.Text = "Record row";
            this.btnRecordRow.UseVisualStyleBackColor = false;
            this.btnRecordRow.Click += new System.EventHandler(this.btnRecordRow_Click);
            this.nudRecOffset.DecimalPlaces = 1;
            this.nudRecOffset.Font = new System.Drawing.Font("Tahoma", 12F);
            this.nudRecOffset.Increment = new decimal(new int[] { 5, 0, 0, 65536 });
            this.nudRecOffset.Location = new System.Drawing.Point(835, 486);
            this.nudRecOffset.Maximum = new decimal(new int[] { 10, 0, 0, 0 });
            this.nudRecOffset.Name = "nudRecOffset";
            this.nudRecOffset.Size = new System.Drawing.Size(70, 28);
            this.nudRecOffset.Click += new System.EventHandler(this.nudOrchard_Click);
            this.nudRecOffset.Value = new decimal(new int[] { 2, 0, 0, 0 });
            this.btnRecSide.FlatStyle = System.Windows.Forms.FlatStyle.Flat;
            this.btnRecSide.Font = new System.Drawing.Font("Tahoma", 11.25F, System.Drawing.FontStyle.Bold);
            this.btnRecSide.Location = new System.Drawing.Point(908, 486);
            this.btnRecSide.Name = "btnRecSide";
            this.btnRecSide.Size = new System.Drawing.Size(72, 28);
            this.btnRecSide.Text = "R →";
            this.btnRecSide.UseVisualStyleBackColor = true;
            this.btnRecSide.Click += new System.EventHandler(this.btnRecSide_Click);
            //
            // btnNewRow
            //
            this.btnNewRow.BackColor = System.Drawing.Color.Honeydew;
            this.btnNewRow.FlatStyle = System.Windows.Forms.FlatStyle.Flat;
            this.btnNewRow.Font = new System.Drawing.Font("Tahoma", 11.25F, System.Drawing.FontStyle.Bold);
            this.btnNewRow.Location = new System.Drawing.Point(684, 548);
            this.btnNewRow.Name = "btnNewRow";
            this.btnNewRow.Size = new System.Drawing.Size(96, 56);
            this.btnNewRow.TabIndex = 21;
            this.btnNewRow.Text = "New row";
            this.btnNewRow.UseVisualStyleBackColor = false;
            this.btnNewRow.Click += new System.EventHandler(this.btnNewRow_Click);
            //
            // btnEdit
            //
            this.btnEdit.BackColor = System.Drawing.Color.AliceBlue;
            this.btnEdit.FlatStyle = System.Windows.Forms.FlatStyle.Flat;
            this.btnEdit.Font = new System.Drawing.Font("Tahoma", 11.25F, System.Drawing.FontStyle.Bold);
            this.btnEdit.Location = new System.Drawing.Point(786, 548);
            this.btnEdit.Name = "btnEdit";
            this.btnEdit.Size = new System.Drawing.Size(96, 56);
            this.btnEdit.TabIndex = 22;
            this.btnEdit.Text = "Edit row";
            this.btnEdit.UseVisualStyleBackColor = false;
            this.btnEdit.Click += new System.EventHandler(this.btnEdit_Click);
            //
            // btnPrevRow
            //
            this.btnPrevRow.Enabled = false;
            this.btnPrevRow.Font = new System.Drawing.Font("Tahoma", 14F, System.Drawing.FontStyle.Bold);
            this.btnPrevRow.Location = new System.Drawing.Point(684, 640);
            this.btnPrevRow.Name = "btnPrevRow";
            this.btnPrevRow.Size = new System.Drawing.Size(90, 46);
            this.btnPrevRow.TabIndex = 23;
            this.btnPrevRow.Text = "|◄◄";
            this.btnPrevRow.UseVisualStyleBackColor = true;
            this.btnPrevRow.Click += new System.EventHandler(this.btnPrevRow_Click);
            //
            // btnDeleteRow
            //
            this.btnDeleteRow.Enabled = false;
            this.btnDeleteRow.Font = new System.Drawing.Font("Tahoma", 12F, System.Drawing.FontStyle.Bold);
            this.btnDeleteRow.Location = new System.Drawing.Point(790, 640);
            this.btnDeleteRow.Name = "btnDeleteRow";
            this.btnDeleteRow.Size = new System.Drawing.Size(90, 46);
            this.btnDeleteRow.TabIndex = 24;
            this.btnDeleteRow.Text = "Del";
            this.btnDeleteRow.UseVisualStyleBackColor = true;
            this.btnDeleteRow.Click += new System.EventHandler(this.btnDeleteRow_Click);
            //
            // btnNextRow
            //
            this.btnNextRow.Enabled = false;
            this.btnNextRow.Font = new System.Drawing.Font("Tahoma", 14F, System.Drawing.FontStyle.Bold);
            this.btnNextRow.Location = new System.Drawing.Point(896, 640);
            this.btnNextRow.Name = "btnNextRow";
            this.btnNextRow.Size = new System.Drawing.Size(90, 46);
            this.btnNextRow.TabIndex = 25;
            this.btnNextRow.Text = "►►|";
            this.btnNextRow.UseVisualStyleBackColor = true;
            this.btnNextRow.Click += new System.EventHandler(this.btnNextRow_Click);
            //
            // btnCancel
            //
            this.btnCancel.BackColor = System.Drawing.Color.MistyRose;
            this.btnCancel.Font = new System.Drawing.Font("Tahoma", 11.25F, System.Drawing.FontStyle.Bold);
            this.btnCancel.Location = new System.Drawing.Point(790, 700);
            this.btnCancel.Name = "btnCancel";
            this.btnCancel.Size = new System.Drawing.Size(90, 50);
            this.btnCancel.TabIndex = 26;
            this.btnCancel.Text = "✕";
            this.btnCancel.UseVisualStyleBackColor = false;
            this.btnCancel.Click += new System.EventHandler(this.btnCancel_Click);
            //
            // btnAccept
            //
            this.btnAccept.BackColor = System.Drawing.Color.Honeydew;
            this.btnAccept.Font = new System.Drawing.Font("Tahoma", 11.25F, System.Drawing.FontStyle.Bold);
            this.btnAccept.Location = new System.Drawing.Point(896, 700);
            this.btnAccept.Name = "btnAccept";
            this.btnAccept.Size = new System.Drawing.Size(90, 50);
            this.btnAccept.TabIndex = 27;
            this.btnAccept.Text = "✓";
            this.btnAccept.UseVisualStyleBackColor = false;
            this.btnAccept.Click += new System.EventHandler(this.btnAccept_Click);
            //
            // btnNewOrchard (planting: lay out a brand new orchard from one reference
            // line + the spacing settings; opens pnlGen over the row-management column)
            //
            this.btnNewOrchard.BackColor = System.Drawing.Color.PaleGreen;
            this.btnNewOrchard.FlatStyle = System.Windows.Forms.FlatStyle.Flat;
            this.btnNewOrchard.Font = new System.Drawing.Font("Tahoma", 10F, System.Drawing.FontStyle.Bold);
            this.btnNewOrchard.Location = new System.Drawing.Point(684, 53);
            this.btnNewOrchard.Name = "btnNewOrchard";
            this.btnNewOrchard.Size = new System.Drawing.Size(300, 36);
            this.btnNewOrchard.TabIndex = 39;
            this.btnNewOrchard.Text = "New orchard (planting)";
            this.btnNewOrchard.UseVisualStyleBackColor = false;
            this.btnNewOrchard.Click += new System.EventHandler(this.btnNewOrchard_Click);
            //
            // btnTreesOnRow (planting: re-space the seedlings along the active row after
            // it was hand-edited, and put seedlings on a row drawn by hand for an odd
            // corner of the field). Tucked into the free corner of the arrow pad.
            //
            this.btnTreesOnRow.BackColor = System.Drawing.Color.OldLace;
            this.btnTreesOnRow.Enabled = false;
            this.btnTreesOnRow.FlatStyle = System.Windows.Forms.FlatStyle.Flat;
            this.btnTreesOnRow.Font = new System.Drawing.Font("Tahoma", 8.25F, System.Drawing.FontStyle.Bold);
            this.btnTreesOnRow.Location = new System.Drawing.Point(772, 220);
            this.btnTreesOnRow.Name = "btnTreesOnRow";
            this.btnTreesOnRow.Size = new System.Drawing.Size(78, 58);
            this.btnTreesOnRow.TabIndex = 41;
            this.btnTreesOnRow.Text = "Trees\r\non row";
            this.btnTreesOnRow.UseVisualStyleBackColor = false;
            //SIM 0729 t2: the generator lays out the whole orchard, so re-spacing one row
            //by hand only ever REDUCED the seedling count - hidden until it is redefined.
            //COrchard.PlaceTreesOnRow stays in the code.
            this.btnTreesOnRow.Visible = false;
            this.btnTreesOnRow.Click += new System.EventHandler(this.btnTreesOnRow_Click);
            //
            // pnlGen - planting layout settings; sits over the row buttons so the drawing
            // tools (arrow pad, add/delete point, measure) stay reachable above it
            //
            this.pnlGen.BackColor = System.Drawing.Color.Honeydew;
            this.pnlGen.BorderStyle = System.Windows.Forms.BorderStyle.FixedSingle;
            this.pnlGen.Location = new System.Drawing.Point(680, 366);
            this.pnlGen.Name = "pnlGen";
            this.pnlGen.Size = new System.Drawing.Size(304, 330);
            this.pnlGen.TabIndex = 40;
            this.pnlGen.Visible = false;
            this.lblGenCap.Font = new System.Drawing.Font("Tahoma", 9.75F, System.Drawing.FontStyle.Bold);
            this.lblGenCap.ForeColor = System.Drawing.Color.DarkGreen;
            this.lblGenCap.Location = new System.Drawing.Point(8, 4);
            this.lblGenCap.Name = "lblGenCap";
            this.lblGenCap.Size = new System.Drawing.Size(288, 20);
            this.lblGenCap.Text = "NEW ORCHARD - planting layout";
            this.lblGenRowSpace.Font = new System.Drawing.Font("Tahoma", 9.75F);
            this.lblGenRowSpace.Location = new System.Drawing.Point(6, 30);
            this.lblGenRowSpace.Name = "lblGenRowSpace";
            this.lblGenRowSpace.Size = new System.Drawing.Size(160, 24);
            this.lblGenRowSpace.Text = "Row spacing (m)";
            this.nudGenRowSpace.DecimalPlaces = 2;
            this.nudGenRowSpace.Font = new System.Drawing.Font("Tahoma", 11F);
            this.nudGenRowSpace.Increment = new decimal(new int[] { 1, 0, 0, 65536 });
            this.nudGenRowSpace.Location = new System.Drawing.Point(172, 26);
            this.nudGenRowSpace.Maximum = new decimal(new int[] { 30, 0, 0, 0 });
            this.nudGenRowSpace.Minimum = new decimal(new int[] { 5, 0, 0, 65536 });
            this.nudGenRowSpace.Name = "nudGenRowSpace";
            this.nudGenRowSpace.Size = new System.Drawing.Size(118, 28);
            this.nudGenRowSpace.Click += new System.EventHandler(this.nudOrchard_Click);
            this.nudGenRowSpace.Value = new decimal(new int[] { 4, 0, 0, 0 });
            this.lblGenTreeSpace.Font = new System.Drawing.Font("Tahoma", 9.75F);
            this.lblGenTreeSpace.Location = new System.Drawing.Point(6, 60);
            this.lblGenTreeSpace.Name = "lblGenTreeSpace";
            this.lblGenTreeSpace.Size = new System.Drawing.Size(160, 24);
            this.lblGenTreeSpace.Text = "Tree spacing (m)";
            this.nudGenTreeSpace.DecimalPlaces = 2;
            this.nudGenTreeSpace.Font = new System.Drawing.Font("Tahoma", 11F);
            this.nudGenTreeSpace.Increment = new decimal(new int[] { 1, 0, 0, 65536 });
            this.nudGenTreeSpace.Location = new System.Drawing.Point(172, 56);
            this.nudGenTreeSpace.Maximum = new decimal(new int[] { 30, 0, 0, 0 });
            this.nudGenTreeSpace.Minimum = new decimal(new int[] { 3, 0, 0, 65536 });
            this.nudGenTreeSpace.Name = "nudGenTreeSpace";
            this.nudGenTreeSpace.Size = new System.Drawing.Size(118, 28);
            this.nudGenTreeSpace.Click += new System.EventHandler(this.nudOrchard_Click);
            this.nudGenTreeSpace.Value = new decimal(new int[] { 3, 0, 0, 0 });
            this.lblGenHeadland.Font = new System.Drawing.Font("Tahoma", 9.75F);
            this.lblGenHeadland.Location = new System.Drawing.Point(6, 90);
            this.lblGenHeadland.Name = "lblGenHeadland";
            this.lblGenHeadland.Size = new System.Drawing.Size(160, 24);
            this.lblGenHeadland.Text = "Headland - row end (m)";
            this.nudGenHeadland.DecimalPlaces = 1;
            this.nudGenHeadland.Font = new System.Drawing.Font("Tahoma", 11F);
            this.nudGenHeadland.Increment = new decimal(new int[] { 5, 0, 0, 65536 });
            this.nudGenHeadland.Location = new System.Drawing.Point(172, 86);
            this.nudGenHeadland.Maximum = new decimal(new int[] { 50, 0, 0, 0 });
            this.nudGenHeadland.Name = "nudGenHeadland";
            this.nudGenHeadland.Size = new System.Drawing.Size(118, 28);
            this.nudGenHeadland.Click += new System.EventHandler(this.nudOrchard_Click);
            this.nudGenHeadland.Value = new decimal(new int[] { 6, 0, 0, 0 });
            this.lblGenSetback.Font = new System.Drawing.Font("Tahoma", 9.75F);
            this.lblGenSetback.Location = new System.Drawing.Point(6, 120);
            this.lblGenSetback.Name = "lblGenSetback";
            this.lblGenSetback.Size = new System.Drawing.Size(160, 24);
            this.lblGenSetback.Text = "Tree to boundary (m)";
            this.nudGenSetback.DecimalPlaces = 1;
            this.nudGenSetback.Font = new System.Drawing.Font("Tahoma", 11F);
            this.nudGenSetback.Increment = new decimal(new int[] { 5, 0, 0, 65536 });
            this.nudGenSetback.Location = new System.Drawing.Point(172, 116);
            this.nudGenSetback.Maximum = new decimal(new int[] { 50, 0, 0, 0 });
            this.nudGenSetback.Name = "nudGenSetback";
            this.nudGenSetback.Size = new System.Drawing.Size(118, 28);
            this.nudGenSetback.Click += new System.EventHandler(this.nudOrchard_Click);
            this.nudGenSetback.Value = new decimal(new int[] { 2, 0, 0, 0 });
            this.chkGenStagger.Font = new System.Drawing.Font("Tahoma", 9.75F);
            this.chkGenStagger.Location = new System.Drawing.Point(6, 150);
            this.chkGenStagger.Name = "chkGenStagger";
            this.chkGenStagger.Size = new System.Drawing.Size(290, 24);
            this.chkGenStagger.Text = "Stagger rows (half tree spacing)";
            this.btnGenLine.BackColor = System.Drawing.Color.AliceBlue;
            this.btnGenLine.FlatStyle = System.Windows.Forms.FlatStyle.Flat;
            this.btnGenLine.Font = new System.Drawing.Font("Tahoma", 9.75F, System.Drawing.FontStyle.Bold);
            this.btnGenLine.Location = new System.Drawing.Point(6, 178);
            this.btnGenLine.Name = "btnGenLine";
            this.btnGenLine.Size = new System.Drawing.Size(140, 46);
            this.btnGenLine.Text = "1 - Draw line";
            this.btnGenLine.UseVisualStyleBackColor = false;
            this.btnGenLine.Click += new System.EventHandler(this.btnGenLine_Click);
            this.btnGenerate.BackColor = System.Drawing.Color.LightGoldenrodYellow;
            this.btnGenerate.FlatStyle = System.Windows.Forms.FlatStyle.Flat;
            this.btnGenerate.Font = new System.Drawing.Font("Tahoma", 9.75F, System.Drawing.FontStyle.Bold);
            this.btnGenerate.Location = new System.Drawing.Point(152, 178);
            this.btnGenerate.Name = "btnGenerate";
            this.btnGenerate.Size = new System.Drawing.Size(144, 46);
            this.btnGenerate.Text = "2 - Generate";
            this.btnGenerate.UseVisualStyleBackColor = false;
            this.btnGenerate.Click += new System.EventHandler(this.btnGenerate_Click);
            this.lblGenResult.Font = new System.Drawing.Font("Tahoma", 9.75F, System.Drawing.FontStyle.Bold);
            this.lblGenResult.ForeColor = System.Drawing.Color.DimGray;
            this.lblGenResult.Location = new System.Drawing.Point(6, 230);
            this.lblGenResult.Name = "lblGenResult";
            this.lblGenResult.Size = new System.Drawing.Size(290, 54);
            this.btnGenClose.Font = new System.Drawing.Font("Tahoma", 9.75F, System.Drawing.FontStyle.Bold);
            this.btnGenClose.Location = new System.Drawing.Point(6, 288);
            this.btnGenClose.Name = "btnGenClose";
            this.btnGenClose.Size = new System.Drawing.Size(290, 36);
            this.btnGenClose.Text = "Close";
            this.btnGenClose.UseVisualStyleBackColor = true;
            this.btnGenClose.Click += new System.EventHandler(this.btnGenClose_Click);
            this.pnlGen.Controls.Add(this.lblGenCap);
            this.pnlGen.Controls.Add(this.lblGenRowSpace);
            this.pnlGen.Controls.Add(this.nudGenRowSpace);
            this.pnlGen.Controls.Add(this.lblGenTreeSpace);
            this.pnlGen.Controls.Add(this.nudGenTreeSpace);
            this.pnlGen.Controls.Add(this.lblGenHeadland);
            this.pnlGen.Controls.Add(this.nudGenHeadland);
            this.pnlGen.Controls.Add(this.lblGenSetback);
            this.pnlGen.Controls.Add(this.nudGenSetback);
            this.pnlGen.Controls.Add(this.chkGenStagger);
            this.pnlGen.Controls.Add(this.btnGenLine);
            this.pnlGen.Controls.Add(this.btnGenerate);
            this.pnlGen.Controls.Add(this.lblGenResult);
            this.pnlGen.Controls.Add(this.btnGenClose);
            //
            // FormOrchardPlanner
            //
            this.AutoScaleDimensions = new System.Drawing.SizeF(6F, 13F);
            this.AutoScaleMode = System.Windows.Forms.AutoScaleMode.Font;
            this.BackColor = System.Drawing.Color.WhiteSmoke;
            this.ClientSize = new System.Drawing.Size(1000, 760);
            this.Controls.Add(this.btnRecordRow);
            this.Controls.Add(this.nudRecOffset);
            this.Controls.Add(this.btnRecSide);
            this.Controls.Add(this.btnZoomIn);
            this.Controls.Add(this.btnZoomOut);
            this.Controls.Add(this.btnZoomRestore);
            this.Controls.Add(this.btnSat);
            this.Controls.Add(this.btnWinFull);
            this.Controls.Add(this.btnWinClose);
            this.Controls.Add(this.lblLegCap);
            this.Controls.Add(this.lblLegRow);
            this.Controls.Add(this.lblLegMid);
            this.Controls.Add(this.lblLegTree);
            this.Controls.Add(this.lblName);
            this.Controls.Add(this.tboxName);
            this.Controls.Add(this.lblActiveOp);
            this.Controls.Add(this.btnFine);
            this.Controls.Add(this.btnCoarse);
            this.Controls.Add(this.btnMeasure);
            this.Controls.Add(this.lblMeasure);
            this.Controls.Add(this.btnNavUp);
            this.Controls.Add(this.btnNavLeft);
            this.Controls.Add(this.btnPoint);
            this.Controls.Add(this.btnNavRight);
            this.Controls.Add(this.btnNavDown);
            this.Controls.Add(this.lblPointMode);
            this.Controls.Add(this.btnLimitEdit);
            this.Controls.Add(this.btnAddPoint);
            this.Controls.Add(this.btnDeletePoint);
            this.Controls.Add(this.btnImportKml);
            this.Controls.Add(this.btnMakePath);
            this.Controls.Add(this.lblWarn);
            this.Controls.Add(this.btnNewRow);
            this.Controls.Add(this.btnEdit);
            this.Controls.Add(this.btnPrevRow);
            this.Controls.Add(this.btnDeleteRow);
            this.Controls.Add(this.btnNextRow);
            this.Controls.Add(this.btnCancel);
            this.Controls.Add(this.btnAccept);
            this.Controls.Add(this.btnNewOrchard);
            //
            // Row filter (t9): thin + smooth on the row's control points, magenta preview
            //
            this.lblFilterCap.AutoSize = true;
            this.lblFilterCap.Font = new System.Drawing.Font("Tahoma", 9.75F, System.Drawing.FontStyle.Bold);
            this.lblFilterCap.Location = new System.Drawing.Point(684, 344);
            this.lblFilterCap.Name = "lblFilterCap";
            this.lblFilterCap.Size = new System.Drawing.Size(120, 16);
            this.lblFilterCap.Text = "Filter row";
            this.lblThin.AutoSize = true;
            this.lblThin.Font = new System.Drawing.Font("Tahoma", 8.25F);
            this.lblThin.Location = new System.Drawing.Point(684, 414);
            this.lblThin.Name = "lblThin";
            this.lblThin.Size = new System.Drawing.Size(90, 14);
            this.lblThin.Text = "Thin (m)";
            this.btnThinDown.Font = new System.Drawing.Font("Tahoma", 12F, System.Drawing.FontStyle.Bold);
            this.btnThinDown.Location = new System.Drawing.Point(684, 430);
            this.btnThinDown.Name = "btnThinDown";
            this.btnThinDown.Size = new System.Drawing.Size(44, 40);
            this.btnThinDown.Text = "−";
            this.btnThinDown.UseVisualStyleBackColor = true;
            this.btnThinDown.Click += new System.EventHandler(this.btnThinDown_Click);
            this.lblThinVal.Font = new System.Drawing.Font("Tahoma", 12F, System.Drawing.FontStyle.Bold);
            this.lblThinVal.Location = new System.Drawing.Point(730, 440);
            this.lblThinVal.Name = "lblThinVal";
            this.lblThinVal.Size = new System.Drawing.Size(40, 22);
            this.lblThinVal.Text = "--";
            this.lblThinVal.TextAlign = System.Drawing.ContentAlignment.MiddleCenter;
            this.btnThinUp.Font = new System.Drawing.Font("Tahoma", 12F, System.Drawing.FontStyle.Bold);
            this.btnThinUp.Location = new System.Drawing.Point(772, 430);
            this.btnThinUp.Name = "btnThinUp";
            this.btnThinUp.Size = new System.Drawing.Size(44, 40);
            this.btnThinUp.Text = "+";
            this.btnThinUp.UseVisualStyleBackColor = true;
            this.btnThinUp.Click += new System.EventHandler(this.btnThinUp_Click);
            this.lblSmooth.AutoSize = true;
            this.lblSmooth.Font = new System.Drawing.Font("Tahoma", 8.25F);
            this.lblSmooth.Location = new System.Drawing.Point(838, 414);
            this.lblSmooth.Name = "lblSmooth";
            this.lblSmooth.Size = new System.Drawing.Size(90, 14);
            this.lblSmooth.Text = "Smooth";
            this.btnSmoothDown.Font = new System.Drawing.Font("Tahoma", 12F, System.Drawing.FontStyle.Bold);
            this.btnSmoothDown.Location = new System.Drawing.Point(838, 430);
            this.btnSmoothDown.Name = "btnSmoothDown";
            this.btnSmoothDown.Size = new System.Drawing.Size(44, 40);
            this.btnSmoothDown.Text = "−";
            this.btnSmoothDown.UseVisualStyleBackColor = true;
            this.btnSmoothDown.Click += new System.EventHandler(this.btnSmoothDown_Click);
            this.lblSmoothVal.Font = new System.Drawing.Font("Tahoma", 12F, System.Drawing.FontStyle.Bold);
            this.lblSmoothVal.Location = new System.Drawing.Point(884, 440);
            this.lblSmoothVal.Name = "lblSmoothVal";
            this.lblSmoothVal.Size = new System.Drawing.Size(40, 22);
            this.lblSmoothVal.Text = "--";
            this.lblSmoothVal.TextAlign = System.Drawing.ContentAlignment.MiddleCenter;
            this.btnSmoothUp.Font = new System.Drawing.Font("Tahoma", 12F, System.Drawing.FontStyle.Bold);
            this.btnSmoothUp.Location = new System.Drawing.Point(926, 430);
            this.btnSmoothUp.Name = "btnSmoothUp";
            this.btnSmoothUp.Size = new System.Drawing.Size(44, 40);
            this.btnSmoothUp.Text = "+";
            this.btnSmoothUp.UseVisualStyleBackColor = true;
            this.btnSmoothUp.Click += new System.EventHandler(this.btnSmoothUp_Click);
            this.btnFilterApply.BackColor = System.Drawing.Color.Honeydew;
            this.btnFilterApply.Enabled = false;
            this.btnFilterApply.FlatStyle = System.Windows.Forms.FlatStyle.Flat;
            this.btnFilterApply.Font = new System.Drawing.Font("Tahoma", 9.75F, System.Drawing.FontStyle.Bold);
            this.btnFilterApply.Location = new System.Drawing.Point(835, 366);
            this.btnFilterApply.Name = "btnFilterApply";
            this.btnFilterApply.Size = new System.Drawing.Size(145, 44);
            this.btnFilterApply.Text = "Apply filter";
            this.btnFilterApply.UseVisualStyleBackColor = false;
            this.btnFilterApply.Click += new System.EventHandler(this.btnFilterApply_Click);
            //
            // Reference point (SIM 0730): where the measuring point sits relative to the tractor -
            // lateral + side, longitudinal + direction. Everything this window measures (the
            // recorded row line, a marked tree) comes from it, so mapping with a pole needs no
            // change to the implement profile.
            //
            this.nudRefLong.DecimalPlaces = 1;
            this.nudRefLong.Font = new System.Drawing.Font("Tahoma", 12F);
            this.nudRefLong.Increment = new decimal(new int[] { 1, 0, 0, 65536 });
            this.nudRefLong.Location = new System.Drawing.Point(835, 518);
            this.nudRefLong.Maximum = new decimal(new int[] { 15, 0, 0, 0 });
            this.nudRefLong.Name = "nudRefLong";
            this.nudRefLong.Size = new System.Drawing.Size(70, 28);
            this.nudRefLong.Click += new System.EventHandler(this.nudOrchard_Click);
            this.btnRefDir.Font = new System.Drawing.Font("Tahoma", 11F, System.Drawing.FontStyle.Bold);
            this.btnRefDir.Location = new System.Drawing.Point(908, 518);
            this.btnRefDir.Name = "btnRefDir";
            this.btnRefDir.Size = new System.Drawing.Size(72, 28);
            this.btnRefDir.Text = "^ F";
            this.btnRefDir.UseVisualStyleBackColor = true;
            this.btnRefDir.Click += new System.EventHandler(this.btnRefDir_Click);
            //
            // Tree marking (SIM 0730): big button over the map, and the recording-only controls
            // that take the place of the point/import/filter block while a row is being recorded.
            //
            this.btnMarkTree.BackColor = System.Drawing.Color.Honeydew;
            this.btnMarkTree.FlatStyle = System.Windows.Forms.FlatStyle.Flat;
            this.btnMarkTree.Font = new System.Drawing.Font("Tahoma", 14F, System.Drawing.FontStyle.Bold);
            this.btnMarkTree.Location = new System.Drawing.Point(24, 560);
            this.btnMarkTree.Name = "btnMarkTree";
            this.btnMarkTree.Size = new System.Drawing.Size(150, 88);
            this.btnMarkTree.Text = "Tree";
            this.btnMarkTree.UseVisualStyleBackColor = false;
            this.btnMarkTree.Visible = false;
            this.btnMarkTree.Click += new System.EventHandler(this.btnMarkTree_Click);
            this.btnDeleteLastTree.BackColor = System.Drawing.Color.MistyRose;
            this.btnDeleteLastTree.FlatStyle = System.Windows.Forms.FlatStyle.Flat;
            this.btnDeleteLastTree.Font = new System.Drawing.Font("Tahoma", 9.75F, System.Drawing.FontStyle.Bold);
            this.btnDeleteLastTree.Location = new System.Drawing.Point(684, 316);
            this.btnDeleteLastTree.Name = "btnDeleteLastTree";
            this.btnDeleteLastTree.Size = new System.Drawing.Size(296, 44);
            this.btnDeleteLastTree.Text = "Delete last tree";
            this.btnDeleteLastTree.UseVisualStyleBackColor = false;
            this.btnDeleteLastTree.Visible = false;
            this.btnDeleteLastTree.Click += new System.EventHandler(this.btnDeleteLastTree_Click);
            this.lblTreeMinDist.AutoSize = true;
            this.lblTreeMinDist.Font = new System.Drawing.Font("Tahoma", 8.25F);
            this.lblTreeMinDist.Location = new System.Drawing.Point(684, 376);
            this.lblTreeMinDist.Name = "lblTreeMinDist";
            this.lblTreeMinDist.Size = new System.Drawing.Size(140, 14);
            this.lblTreeMinDist.Text = "Min tree spacing (m)";
            this.lblTreeMinDist.Visible = false;
            this.nudTreeMinDist.DecimalPlaces = 1;
            this.nudTreeMinDist.Font = new System.Drawing.Font("Tahoma", 12F);
            this.nudTreeMinDist.Increment = new decimal(new int[] { 5, 0, 0, 65536 });
            this.nudTreeMinDist.Location = new System.Drawing.Point(835, 370);
            this.nudTreeMinDist.Maximum = new decimal(new int[] { 20, 0, 0, 0 });
            this.nudTreeMinDist.Name = "nudTreeMinDist";
            this.nudTreeMinDist.Size = new System.Drawing.Size(70, 30);
            this.nudTreeMinDist.Visible = false;
            this.nudTreeMinDist.Click += new System.EventHandler(this.nudOrchard_Click);
            this.btnKeyTrigger.FlatStyle = System.Windows.Forms.FlatStyle.Flat;
            this.btnKeyTrigger.Font = new System.Drawing.Font("Tahoma", 9F);
            this.btnKeyTrigger.Location = new System.Drawing.Point(684, 412);
            this.btnKeyTrigger.Name = "btnKeyTrigger";
            this.btnKeyTrigger.Size = new System.Drawing.Size(145, 38);
            this.btnKeyTrigger.Text = "Key trigger";
            this.btnKeyTrigger.UseVisualStyleBackColor = true;
            this.btnKeyTrigger.Visible = false;
            this.btnKeyTrigger.Click += new System.EventHandler(this.btnKeyTrigger_Click);
            this.lblKeyTrigger.AutoSize = true;
            this.lblKeyTrigger.Font = new System.Drawing.Font("Tahoma", 7.5F);
            this.lblKeyTrigger.ForeColor = System.Drawing.Color.DimGray;
            this.lblKeyTrigger.Location = new System.Drawing.Point(686, 452);
            this.lblKeyTrigger.Name = "lblKeyTrigger";
            this.lblKeyTrigger.Size = new System.Drawing.Size(140, 12);
            this.lblKeyTrigger.Text = "";
            this.lblKeyTrigger.Visible = false;
            this.btnEditTrees.BackColor = System.Drawing.Color.AliceBlue;
            this.btnEditTrees.FlatStyle = System.Windows.Forms.FlatStyle.Flat;
            this.btnEditTrees.Font = new System.Drawing.Font("Tahoma", 9.75F, System.Drawing.FontStyle.Bold);
            this.btnEditTrees.Location = new System.Drawing.Point(888, 548);
            this.btnEditTrees.Name = "btnEditTrees";
            this.btnEditTrees.Size = new System.Drawing.Size(96, 56);
            this.btnEditTrees.Text = "Edit trees";
            this.btnEditTrees.UseVisualStyleBackColor = false;
            this.btnEditTrees.Click += new System.EventHandler(this.btnEditTrees_Click);
            this.Controls.Add(this.btnEditTrees);
            this.Controls.Add(this.btnMarkTree);
            this.Controls.Add(this.btnDeleteLastTree);
            this.Controls.Add(this.lblTreeMinDist);
            this.Controls.Add(this.nudTreeMinDist);
            this.Controls.Add(this.btnKeyTrigger);
            this.Controls.Add(this.lblKeyTrigger);
            this.Controls.Add(this.nudRefLong);
            this.Controls.Add(this.btnRefDir);
            this.Controls.Add(this.lblThin);
            this.Controls.Add(this.btnThinDown);
            this.Controls.Add(this.lblThinVal);
            this.Controls.Add(this.btnThinUp);
            this.Controls.Add(this.lblSmooth);
            this.Controls.Add(this.btnSmoothDown);
            this.Controls.Add(this.lblSmoothVal);
            this.Controls.Add(this.btnSmoothUp);
            this.Controls.Add(this.btnFilterApply);
            this.Controls.Add(this.btnTreesOnRow);
            this.Controls.Add(this.pnlGen);
            this.Controls.Add(this.oglSelf);
            this.FormBorderStyle = System.Windows.Forms.FormBorderStyle.None;
            this.MinimumSize = new System.Drawing.Size(820, 560);
            this.Name = "FormOrchardPlanner";
            this.StartPosition = System.Windows.Forms.FormStartPosition.CenterParent;
            this.Text = "Orchard Planner";
            this.Load += new System.EventHandler(this.FormOrchardPlanner_Load);
            this.Resize += new System.EventHandler(this.FormOrchardPlanner_Resize);
            this.FormClosing += new System.Windows.Forms.FormClosingEventHandler(this.FormOrchardPlanner_FormClosing);
            this.ResumeLayout(false);
            this.PerformLayout();
        }

        #endregion

        private OpenTK.GLControl oglSelf;
        private System.Windows.Forms.Label lblActiveOp;
        private System.Windows.Forms.Button btnZoomIn;
        private System.Windows.Forms.Button btnZoomOut;
        private System.Windows.Forms.Button btnZoomRestore;
        private System.Windows.Forms.CheckBox btnSat;
        private System.Windows.Forms.Button btnWinFull;
        private System.Windows.Forms.Button btnWinClose;
        private System.Windows.Forms.Label lblName;
        private System.Windows.Forms.TextBox tboxName;
        private System.Windows.Forms.Label lblLegCap;
        private System.Windows.Forms.Label lblLegRow;
        private System.Windows.Forms.Label lblLegMid;
        private System.Windows.Forms.Label lblLegTree;
        private System.Windows.Forms.Button btnFine;
        private System.Windows.Forms.Button btnCoarse;
        private System.Windows.Forms.Button btnMeasure;
        private System.Windows.Forms.Label lblMeasure;
        private System.Windows.Forms.Button btnNavUp;
        private System.Windows.Forms.Button btnNavDown;
        private System.Windows.Forms.Button btnNavLeft;
        private System.Windows.Forms.Button btnNavRight;
        private System.Windows.Forms.Button btnPoint;
        private System.Windows.Forms.Label lblPointMode;
        private System.Windows.Forms.CheckBox btnAddPoint;
        private System.Windows.Forms.CheckBox btnLimitEdit;
        private System.Windows.Forms.CheckBox btnDeletePoint;
        private System.Windows.Forms.Button btnImportKml;
        private System.Windows.Forms.Button btnMakePath;
        private System.Windows.Forms.Button btnRecordRow;
        private AgOpenGPS.NudlessNumericUpDown nudRecOffset;
        private System.Windows.Forms.Button btnRecSide;
        private System.Windows.Forms.Label lblWarn;
        private System.Windows.Forms.Button btnNewRow;
        private System.Windows.Forms.Button btnEdit;
        private System.Windows.Forms.Button btnPrevRow;
        private System.Windows.Forms.Button btnNextRow;
        private System.Windows.Forms.Button btnDeleteRow;
        private System.Windows.Forms.Button btnCancel;
        private System.Windows.Forms.Button btnAccept;
        //planting: new-orchard layout generator
        private System.Windows.Forms.Button btnNewOrchard;
        private System.Windows.Forms.Button btnTreesOnRow;
        private System.Windows.Forms.Label lblFilterCap;
        private AgOpenGPS.NudlessNumericUpDown nudRefLong;
        private System.Windows.Forms.Button btnRefDir;
        private System.Windows.Forms.Button btnEditTrees;
        private System.Windows.Forms.Button btnMarkTree;
        private System.Windows.Forms.Button btnDeleteLastTree;
        private System.Windows.Forms.Label lblTreeMinDist;
        private AgOpenGPS.NudlessNumericUpDown nudTreeMinDist;
        private System.Windows.Forms.Button btnKeyTrigger;
        private System.Windows.Forms.Label lblKeyTrigger;
        private System.Windows.Forms.Label lblThin;
        private System.Windows.Forms.Button btnThinDown;
        private System.Windows.Forms.Button btnThinUp;
        private System.Windows.Forms.Label lblThinVal;
        private System.Windows.Forms.Label lblSmooth;
        private System.Windows.Forms.Button btnSmoothDown;
        private System.Windows.Forms.Button btnSmoothUp;
        private System.Windows.Forms.Label lblSmoothVal;
        private System.Windows.Forms.Button btnFilterApply;
        private System.Windows.Forms.Panel pnlGen;
        private System.Windows.Forms.Label lblGenCap;
        private System.Windows.Forms.Label lblGenRowSpace;
        private AgOpenGPS.NudlessNumericUpDown nudGenRowSpace;
        private System.Windows.Forms.Label lblGenTreeSpace;
        private AgOpenGPS.NudlessNumericUpDown nudGenTreeSpace;
        private System.Windows.Forms.Label lblGenHeadland;
        private AgOpenGPS.NudlessNumericUpDown nudGenHeadland;
        private System.Windows.Forms.Label lblGenSetback;
        private AgOpenGPS.NudlessNumericUpDown nudGenSetback;
        private System.Windows.Forms.CheckBox chkGenStagger;
        private System.Windows.Forms.Button btnGenLine;
        private System.Windows.Forms.Button btnGenerate;
        private System.Windows.Forms.Label lblGenResult;
        private System.Windows.Forms.Button btnGenClose;
    }
}
