namespace AgOpenGPS
{
    partial class FormSmartAB
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
            this.btnSat = new System.Windows.Forms.CheckBox();
            this.btnWinFull = new System.Windows.Forms.Button();
            this.btnWinClose = new System.Windows.Forms.Button();
            this.lblName = new System.Windows.Forms.Label();
            this.tboxName = new System.Windows.Forms.TextBox();
            this.lblLegCap = new System.Windows.Forms.Label();
            this.lblLegA = new System.Windows.Forms.Label();
            this.lblLegB = new System.Windows.Forms.Label();
            this.lblLegP = new System.Windows.Forms.Label();
            this.lblLegS = new System.Windows.Forms.Label();
            this.btnFine = new System.Windows.Forms.Button();
            this.btnCoarse = new System.Windows.Forms.Button();
            this.btnNavUp = new System.Windows.Forms.Button();
            this.btnNavDown = new System.Windows.Forms.Button();
            this.btnNavLeft = new System.Windows.Forms.Button();
            this.btnNavRight = new System.Windows.Forms.Button();
            this.btnPoint = new System.Windows.Forms.Button();
            this.lblPointMode = new System.Windows.Forms.Label();
            this.btnAddPoint = new System.Windows.Forms.CheckBox();
            this.btnDeletePoint = new System.Windows.Forms.CheckBox();
            this.btnMakeAB = new System.Windows.Forms.Button();
            this.btnMakeBndCurve = new System.Windows.Forms.Button();
            this.btnMakeCurve = new System.Windows.Forms.Button();
            this.lblCheck = new System.Windows.Forms.Label();
            this.tboxCheckCm = new System.Windows.Forms.TextBox();
            this.btnCheck = new System.Windows.Forms.Button();
            this.lblSmoot = new System.Windows.Forms.Label();
            this.btnSmoothDown = new System.Windows.Forms.Button();
            this.btnSmoothUp = new System.Windows.Forms.Button();
            this.btnSmoothApply = new System.Windows.Forms.Button();
            this.lblSmoothVal = new System.Windows.Forms.Label();
            this.btnPrevLine = new System.Windows.Forms.Button();
            this.btnNextLine = new System.Windows.Forms.Button();
            this.btnDeleteLine = new System.Windows.Forms.Button();
            this.btnCancel = new System.Windows.Forms.Button();
            this.btnAccept = new System.Windows.Forms.Button();
            this.btnNew = new System.Windows.Forms.Button();
            this.btnEdit = new System.Windows.Forms.Button();
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
            // btnZoomIn (over the map)
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
            // btnZoomOut (over the map)
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
            // btnSat (over the map, top-right) - placeholder (Phase 3)
            //
            this.btnSat.Appearance = System.Windows.Forms.Appearance.Button;
            this.btnSat.BackColor = System.Drawing.Color.White;
            this.btnSat.FlatAppearance.CheckedBackColor = System.Drawing.Color.MediumAquamarine;
            this.btnSat.FlatStyle = System.Windows.Forms.FlatStyle.Flat;
            this.btnSat.Font = new System.Drawing.Font("Tahoma", 8F, System.Drawing.FontStyle.Bold);
            this.btnSat.Location = new System.Drawing.Point(604, 14);
            this.btnSat.Name = "btnSat";
            this.btnSat.Size = new System.Drawing.Size(56, 50);
            this.btnSat.TabIndex = 4;
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
            this.lblName.Location = new System.Drawing.Point(684, 600);
            this.lblName.Name = "lblName";
            this.lblName.Size = new System.Drawing.Size(43, 16);
            this.lblName.TabIndex = 33;
            this.lblName.Text = "Name";
            //
            // tboxName
            //
            this.tboxName.Font = new System.Drawing.Font("Tahoma", 11.25F);
            this.tboxName.Location = new System.Drawing.Point(740, 596);
            this.tboxName.Name = "tboxName";
            this.tboxName.Size = new System.Drawing.Size(246, 26);
            this.tboxName.TabIndex = 34;
            //
            // legend (marker colours)
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
            this.lblLegA.BackColor = System.Drawing.Color.FromArgb(((int)(((byte)(51)))), ((int)(((byte)(230)))), ((int)(((byte)(255)))));
            this.lblLegA.BorderStyle = System.Windows.Forms.BorderStyle.FixedSingle;
            this.lblLegA.Font = new System.Drawing.Font("Tahoma", 9.75F, System.Drawing.FontStyle.Bold);
            this.lblLegA.Location = new System.Drawing.Point(684, 26);
            this.lblLegA.Name = "lblLegA";
            this.lblLegA.Size = new System.Drawing.Size(30, 24);
            this.lblLegA.TabIndex = 36;
            this.lblLegA.Text = "A";
            this.lblLegA.TextAlign = System.Drawing.ContentAlignment.MiddleCenter;
            //
            this.lblLegB.BackColor = System.Drawing.Color.FromArgb(((int)(((byte)(255)))), ((int)(((byte)(153)))), ((int)(((byte)(26)))));
            this.lblLegB.BorderStyle = System.Windows.Forms.BorderStyle.FixedSingle;
            this.lblLegB.Font = new System.Drawing.Font("Tahoma", 9.75F, System.Drawing.FontStyle.Bold);
            this.lblLegB.Location = new System.Drawing.Point(716, 26);
            this.lblLegB.Name = "lblLegB";
            this.lblLegB.Size = new System.Drawing.Size(30, 24);
            this.lblLegB.TabIndex = 37;
            this.lblLegB.Text = "B";
            this.lblLegB.TextAlign = System.Drawing.ContentAlignment.MiddleCenter;
            //
            this.lblLegP.BackColor = System.Drawing.Color.White;
            this.lblLegP.BorderStyle = System.Windows.Forms.BorderStyle.FixedSingle;
            this.lblLegP.Font = new System.Drawing.Font("Tahoma", 9F, System.Drawing.FontStyle.Bold);
            this.lblLegP.Location = new System.Drawing.Point(748, 26);
            this.lblLegP.Name = "lblLegP";
            this.lblLegP.Size = new System.Drawing.Size(44, 24);
            this.lblLegP.TabIndex = 38;
            this.lblLegP.Text = "Point";
            this.lblLegP.TextAlign = System.Drawing.ContentAlignment.MiddleCenter;
            //
            this.lblLegS.BackColor = System.Drawing.Color.LimeGreen;
            this.lblLegS.BorderStyle = System.Windows.Forms.BorderStyle.FixedSingle;
            this.lblLegS.Font = new System.Drawing.Font("Tahoma", 9F, System.Drawing.FontStyle.Bold);
            this.lblLegS.Location = new System.Drawing.Point(794, 26);
            this.lblLegS.Name = "lblLegS";
            this.lblLegS.Size = new System.Drawing.Size(44, 24);
            this.lblLegS.TabIndex = 39;
            this.lblLegS.Text = "Sel.";
            this.lblLegS.TextAlign = System.Drawing.ContentAlignment.MiddleCenter;
            //
            // btnFine
            //
            this.btnFine.FlatStyle = System.Windows.Forms.FlatStyle.Flat;
            this.btnFine.Font = new System.Drawing.Font("Tahoma", 10F, System.Drawing.FontStyle.Bold);
            this.btnFine.Location = new System.Drawing.Point(684, 100);
            this.btnFine.Name = "btnFine";
            this.btnFine.Size = new System.Drawing.Size(80, 44);
            this.btnFine.TabIndex = 5;
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
            this.btnCoarse.TabIndex = 6;
            this.btnCoarse.Text = "Coarse";
            this.btnCoarse.UseVisualStyleBackColor = false;
            this.btnCoarse.Click += new System.EventHandler(this.btnCoarse_Click);
            //
            // btnNavUp
            //
            this.btnNavUp.FlatStyle = System.Windows.Forms.FlatStyle.Flat;
            this.btnNavUp.Font = new System.Drawing.Font("Tahoma", 20F, System.Drawing.FontStyle.Bold);
            this.btnNavUp.ForeColor = System.Drawing.Color.RoyalBlue;
            this.btnNavUp.Location = new System.Drawing.Point(854, 92);
            this.btnNavUp.Name = "btnNavUp";
            this.btnNavUp.Size = new System.Drawing.Size(60, 60);
            this.btnNavUp.TabIndex = 7;
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
            this.btnNavLeft.TabIndex = 8;
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
            this.btnPoint.TabIndex = 9;
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
            this.btnNavRight.TabIndex = 10;
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
            this.btnNavDown.TabIndex = 11;
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
            this.lblPointMode.TabIndex = 12;
            this.lblPointMode.TextAlign = System.Drawing.ContentAlignment.MiddleCenter;
            //
            // btnAddPoint
            //
            this.btnAddPoint.Appearance = System.Windows.Forms.Appearance.Button;
            this.btnAddPoint.BackColor = System.Drawing.Color.AliceBlue;
            this.btnAddPoint.FlatAppearance.CheckedBackColor = System.Drawing.Color.Gold;
            this.btnAddPoint.FlatStyle = System.Windows.Forms.FlatStyle.Flat;
            this.btnAddPoint.Font = new System.Drawing.Font("Tahoma", 10F, System.Drawing.FontStyle.Bold);
            this.btnAddPoint.Location = new System.Drawing.Point(684, 316);
            this.btnAddPoint.Name = "btnAddPoint";
            this.btnAddPoint.Size = new System.Drawing.Size(145, 44);
            this.btnAddPoint.TabIndex = 13;
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
            this.btnDeletePoint.Location = new System.Drawing.Point(835, 316);
            this.btnDeletePoint.Name = "btnDeletePoint";
            this.btnDeletePoint.Size = new System.Drawing.Size(145, 44);
            this.btnDeletePoint.TabIndex = 14;
            this.btnDeletePoint.Text = "Delete point";
            this.btnDeletePoint.TextAlign = System.Drawing.ContentAlignment.MiddleCenter;
            this.btnDeletePoint.UseVisualStyleBackColor = false;
            this.btnDeletePoint.CheckedChanged += new System.EventHandler(this.btnDeletePoint_CheckedChanged);
            //
            // btnMakeAB (placeholder Phase 2)
            //
            this.btnMakeAB.Enabled = false;
            this.btnMakeAB.Font = new System.Drawing.Font("Tahoma", 9.75F, System.Drawing.FontStyle.Bold);
            this.btnMakeAB.Location = new System.Drawing.Point(684, 372);
            this.btnMakeAB.Name = "btnMakeAB";
            this.btnMakeAB.Size = new System.Drawing.Size(96, 56);
            this.btnMakeAB.TabIndex = 15;
            this.btnMakeAB.Text = "AB line";
            this.btnMakeAB.UseVisualStyleBackColor = true;
            this.btnMakeAB.Click += new System.EventHandler(this.btnMakeAB_Click);
            //
            // btnMakeBndCurve (placeholder Phase 2)
            //
            this.btnMakeBndCurve.Enabled = false;
            this.btnMakeBndCurve.Font = new System.Drawing.Font("Tahoma", 8.25F, System.Drawing.FontStyle.Bold);
            this.btnMakeBndCurve.Location = new System.Drawing.Point(786, 372);
            this.btnMakeBndCurve.Name = "btnMakeBndCurve";
            this.btnMakeBndCurve.Size = new System.Drawing.Size(96, 56);
            this.btnMakeBndCurve.TabIndex = 16;
            this.btnMakeBndCurve.Text = "Curve by Boundary";
            this.btnMakeBndCurve.UseVisualStyleBackColor = true;
            this.btnMakeBndCurve.Click += new System.EventHandler(this.btnMakeBndCurve_Click);
            //
            // btnMakeCurve (placeholder Phase 2)
            //
            this.btnMakeCurve.Enabled = false;
            this.btnMakeCurve.Font = new System.Drawing.Font("Tahoma", 9.75F, System.Drawing.FontStyle.Bold);
            this.btnMakeCurve.Location = new System.Drawing.Point(888, 372);
            this.btnMakeCurve.Name = "btnMakeCurve";
            this.btnMakeCurve.Size = new System.Drawing.Size(96, 56);
            this.btnMakeCurve.TabIndex = 17;
            this.btnMakeCurve.Text = "Curve";
            this.btnMakeCurve.UseVisualStyleBackColor = true;
            this.btnMakeCurve.Click += new System.EventHandler(this.btnMakeCurve_Click);
            //
            // lblCheck (placeholder Phase 4)
            //
            this.lblCheck.AutoSize = true;
            this.lblCheck.Font = new System.Drawing.Font("Tahoma", 9.75F);
            this.lblCheck.Location = new System.Drawing.Point(684, 444);
            this.lblCheck.Name = "lblCheck";
            this.lblCheck.Size = new System.Drawing.Size(124, 16);
            this.lblCheck.TabIndex = 18;
            this.lblCheck.Text = "Check boundary outline";
            //
            // tboxCheckCm (placeholder)
            //
            this.tboxCheckCm.Font = new System.Drawing.Font("Tahoma", 12F);
            this.tboxCheckCm.Location = new System.Drawing.Point(818, 440);
            this.tboxCheckCm.Name = "tboxCheckCm";
            this.tboxCheckCm.Size = new System.Drawing.Size(70, 26);
            this.tboxCheckCm.TabIndex = 19;
            this.tboxCheckCm.Text = "10";
            //
            // btnCheck (placeholder)
            //
            this.btnCheck.Font = new System.Drawing.Font("Tahoma", 9.75F, System.Drawing.FontStyle.Bold);
            this.btnCheck.Location = new System.Drawing.Point(896, 438);
            this.btnCheck.Name = "btnCheck";
            this.btnCheck.Size = new System.Drawing.Size(88, 30);
            this.btnCheck.TabIndex = 20;
            this.btnCheck.Text = "Check (cm)";
            this.btnCheck.UseVisualStyleBackColor = true;
            this.btnCheck.Click += new System.EventHandler(this.btnCheck_Click);
            //
            // lblSmoot (placeholder Phase 5)
            //
            this.lblSmoot.AutoSize = true;
            this.lblSmoot.Font = new System.Drawing.Font("Tahoma", 9.75F);
            this.lblSmoot.Location = new System.Drawing.Point(684, 480);
            this.lblSmoot.Name = "lblSmoot";
            this.lblSmoot.Size = new System.Drawing.Size(46, 16);
            this.lblSmoot.TabIndex = 21;
            this.lblSmoot.Text = "Smooth";
            //
            // btnSmoothDown (placeholder)
            //
            this.btnSmoothDown.Enabled = false;
            this.btnSmoothDown.Font = new System.Drawing.Font("Tahoma", 14F, System.Drawing.FontStyle.Bold);
            this.btnSmoothDown.Location = new System.Drawing.Point(684, 500);
            this.btnSmoothDown.Name = "btnSmoothDown";
            this.btnSmoothDown.Size = new System.Drawing.Size(56, 40);
            this.btnSmoothDown.TabIndex = 22;
            this.btnSmoothDown.Text = "▼";
            this.btnSmoothDown.UseVisualStyleBackColor = true;
            this.btnSmoothDown.Click += new System.EventHandler(this.btnSmoothDown_Click);
            //
            // lblSmoothVal (placeholder)
            //
            this.lblSmoothVal.Font = new System.Drawing.Font("Tahoma", 14F, System.Drawing.FontStyle.Bold);
            this.lblSmoothVal.Location = new System.Drawing.Point(744, 500);
            this.lblSmoothVal.Name = "lblSmoothVal";
            this.lblSmoothVal.Size = new System.Drawing.Size(60, 40);
            this.lblSmoothVal.TabIndex = 23;
            this.lblSmoothVal.Text = "--";
            this.lblSmoothVal.TextAlign = System.Drawing.ContentAlignment.MiddleCenter;
            //
            // btnSmoothUp (placeholder)
            //
            this.btnSmoothUp.Enabled = false;
            this.btnSmoothUp.Font = new System.Drawing.Font("Tahoma", 14F, System.Drawing.FontStyle.Bold);
            this.btnSmoothUp.Location = new System.Drawing.Point(808, 500);
            this.btnSmoothUp.Name = "btnSmoothUp";
            this.btnSmoothUp.Size = new System.Drawing.Size(56, 40);
            this.btnSmoothUp.TabIndex = 24;
            this.btnSmoothUp.Text = "▲";
            this.btnSmoothUp.UseVisualStyleBackColor = true;
            this.btnSmoothUp.Click += new System.EventHandler(this.btnSmoothUp_Click);
            //
            // btnSmoothApply (placeholder)
            //
            this.btnSmoothApply.Enabled = false;
            this.btnSmoothApply.Font = new System.Drawing.Font("Tahoma", 9.75F, System.Drawing.FontStyle.Bold);
            this.btnSmoothApply.Location = new System.Drawing.Point(872, 500);
            this.btnSmoothApply.Name = "btnSmoothApply";
            this.btnSmoothApply.Size = new System.Drawing.Size(112, 40);
            this.btnSmoothApply.TabIndex = 25;
            this.btnSmoothApply.Text = "Apply";
            this.btnSmoothApply.UseVisualStyleBackColor = true;
            this.btnSmoothApply.Click += new System.EventHandler(this.btnSmoothApply_Click);
            //
            // btnNew
            //
            this.btnNew.BackColor = System.Drawing.Color.Honeydew;
            this.btnNew.FlatStyle = System.Windows.Forms.FlatStyle.Flat;
            this.btnNew.Font = new System.Drawing.Font("Tahoma", 11.25F, System.Drawing.FontStyle.Bold);
            this.btnNew.Location = new System.Drawing.Point(684, 548);
            this.btnNew.Name = "btnNew";
            this.btnNew.Size = new System.Drawing.Size(146, 42);
            this.btnNew.TabIndex = 40;
            this.btnNew.Text = "New";
            this.btnNew.UseVisualStyleBackColor = false;
            this.btnNew.Click += new System.EventHandler(this.btnNew_Click);
            //
            // btnEdit
            //
            this.btnEdit.BackColor = System.Drawing.Color.AliceBlue;
            this.btnEdit.FlatStyle = System.Windows.Forms.FlatStyle.Flat;
            this.btnEdit.Font = new System.Drawing.Font("Tahoma", 11.25F, System.Drawing.FontStyle.Bold);
            this.btnEdit.Location = new System.Drawing.Point(838, 548);
            this.btnEdit.Name = "btnEdit";
            this.btnEdit.Size = new System.Drawing.Size(146, 42);
            this.btnEdit.TabIndex = 41;
            this.btnEdit.Text = "Edit";
            this.btnEdit.UseVisualStyleBackColor = false;
            this.btnEdit.Click += new System.EventHandler(this.btnEdit_Click);
            //
            // btnPrevLine (active-line nav)
            //
            this.btnPrevLine.Enabled = false;
            this.btnPrevLine.Font = new System.Drawing.Font("Tahoma", 14F, System.Drawing.FontStyle.Bold);
            this.btnPrevLine.Location = new System.Drawing.Point(684, 640);
            this.btnPrevLine.Name = "btnPrevLine";
            this.btnPrevLine.Size = new System.Drawing.Size(90, 46);
            this.btnPrevLine.TabIndex = 26;
            this.btnPrevLine.Text = "|◄◄";
            this.btnPrevLine.UseVisualStyleBackColor = true;
            this.btnPrevLine.Click += new System.EventHandler(this.btnPrevLine_Click);
            //
            // btnDeleteLine (placeholder)
            //
            this.btnDeleteLine.Enabled = false;
            this.btnDeleteLine.Font = new System.Drawing.Font("Tahoma", 12F, System.Drawing.FontStyle.Bold);
            this.btnDeleteLine.Location = new System.Drawing.Point(790, 640);
            this.btnDeleteLine.Name = "btnDeleteLine";
            this.btnDeleteLine.Size = new System.Drawing.Size(90, 46);
            this.btnDeleteLine.TabIndex = 27;
            this.btnDeleteLine.Text = "Del";
            this.btnDeleteLine.UseVisualStyleBackColor = true;
            this.btnDeleteLine.Click += new System.EventHandler(this.btnDeleteLine_Click);
            //
            // btnNextLine (placeholder)
            //
            this.btnNextLine.Enabled = false;
            this.btnNextLine.Font = new System.Drawing.Font("Tahoma", 14F, System.Drawing.FontStyle.Bold);
            this.btnNextLine.Location = new System.Drawing.Point(896, 640);
            this.btnNextLine.Name = "btnNextLine";
            this.btnNextLine.Size = new System.Drawing.Size(90, 46);
            this.btnNextLine.TabIndex = 28;
            this.btnNextLine.Text = "►►|";
            this.btnNextLine.UseVisualStyleBackColor = true;
            this.btnNextLine.Click += new System.EventHandler(this.btnNextLine_Click);
            //
            // btnCancel
            //
            this.btnCancel.BackColor = System.Drawing.Color.MistyRose;
            this.btnCancel.Font = new System.Drawing.Font("Tahoma", 11.25F, System.Drawing.FontStyle.Bold);
            this.btnCancel.Location = new System.Drawing.Point(790, 700);
            this.btnCancel.Name = "btnCancel";
            this.btnCancel.Size = new System.Drawing.Size(90, 50);
            this.btnCancel.TabIndex = 29;
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
            this.btnAccept.TabIndex = 30;
            this.btnAccept.Text = "✓";
            this.btnAccept.UseVisualStyleBackColor = false;
            this.btnAccept.Click += new System.EventHandler(this.btnAccept_Click);
            //
            // FormSmartAB
            //
            this.AutoScaleDimensions = new System.Drawing.SizeF(6F, 13F);
            this.AutoScaleMode = System.Windows.Forms.AutoScaleMode.Font;
            this.BackColor = System.Drawing.Color.WhiteSmoke;
            this.ClientSize = new System.Drawing.Size(1000, 760);
            this.Controls.Add(this.btnZoomIn);
            this.Controls.Add(this.btnZoomOut);
            this.Controls.Add(this.btnSat);
            this.Controls.Add(this.btnWinFull);
            this.Controls.Add(this.btnWinClose);
            this.Controls.Add(this.lblLegCap);
            this.Controls.Add(this.lblLegA);
            this.Controls.Add(this.lblLegB);
            this.Controls.Add(this.lblLegP);
            this.Controls.Add(this.lblLegS);
            this.Controls.Add(this.lblName);
            this.Controls.Add(this.tboxName);
            this.Controls.Add(this.lblActiveOp);
            this.Controls.Add(this.btnFine);
            this.Controls.Add(this.btnCoarse);
            this.Controls.Add(this.btnNavUp);
            this.Controls.Add(this.btnNavLeft);
            this.Controls.Add(this.btnPoint);
            this.Controls.Add(this.btnNavRight);
            this.Controls.Add(this.btnNavDown);
            this.Controls.Add(this.lblPointMode);
            this.Controls.Add(this.btnAddPoint);
            this.Controls.Add(this.btnDeletePoint);
            this.Controls.Add(this.btnMakeAB);
            this.Controls.Add(this.btnMakeBndCurve);
            this.Controls.Add(this.btnMakeCurve);
            this.Controls.Add(this.lblCheck);
            this.Controls.Add(this.tboxCheckCm);
            this.Controls.Add(this.btnCheck);
            this.Controls.Add(this.lblSmoot);
            this.Controls.Add(this.btnSmoothDown);
            this.Controls.Add(this.lblSmoothVal);
            this.Controls.Add(this.btnSmoothUp);
            this.Controls.Add(this.btnSmoothApply);
            this.Controls.Add(this.btnNew);
            this.Controls.Add(this.btnEdit);
            this.Controls.Add(this.btnPrevLine);
            this.Controls.Add(this.btnDeleteLine);
            this.Controls.Add(this.btnNextLine);
            this.Controls.Add(this.btnCancel);
            this.Controls.Add(this.btnAccept);
            this.Controls.Add(this.oglSelf);
            this.FormBorderStyle = System.Windows.Forms.FormBorderStyle.None;
            this.MinimumSize = new System.Drawing.Size(820, 560);
            this.Name = "FormSmartAB";
            this.StartPosition = System.Windows.Forms.FormStartPosition.CenterParent;
            this.Text = "Smart AB";
            this.Load += new System.EventHandler(this.FormSmartAB_Load);
            this.Resize += new System.EventHandler(this.FormSmartAB_Resize);
            this.FormClosing += new System.Windows.Forms.FormClosingEventHandler(this.FormSmartAB_FormClosing);
            this.ResumeLayout(false);
            this.PerformLayout();
        }

        #endregion

        private OpenTK.GLControl oglSelf;
        private System.Windows.Forms.Label lblActiveOp;
        private System.Windows.Forms.Button btnZoomIn;
        private System.Windows.Forms.Button btnZoomOut;
        private System.Windows.Forms.CheckBox btnSat;
        private System.Windows.Forms.Button btnWinFull;
        private System.Windows.Forms.Button btnWinClose;
        private System.Windows.Forms.Label lblName;
        private System.Windows.Forms.TextBox tboxName;
        private System.Windows.Forms.Label lblLegCap;
        private System.Windows.Forms.Label lblLegA;
        private System.Windows.Forms.Label lblLegB;
        private System.Windows.Forms.Label lblLegP;
        private System.Windows.Forms.Label lblLegS;
        private System.Windows.Forms.Button btnFine;
        private System.Windows.Forms.Button btnCoarse;
        private System.Windows.Forms.Button btnNavUp;
        private System.Windows.Forms.Button btnNavDown;
        private System.Windows.Forms.Button btnNavLeft;
        private System.Windows.Forms.Button btnNavRight;
        private System.Windows.Forms.Button btnPoint;
        private System.Windows.Forms.Label lblPointMode;
        private System.Windows.Forms.CheckBox btnAddPoint;
        private System.Windows.Forms.CheckBox btnDeletePoint;
        private System.Windows.Forms.Button btnMakeAB;
        private System.Windows.Forms.Button btnMakeBndCurve;
        private System.Windows.Forms.Button btnMakeCurve;
        private System.Windows.Forms.Label lblCheck;
        private System.Windows.Forms.TextBox tboxCheckCm;
        private System.Windows.Forms.Button btnCheck;
        private System.Windows.Forms.Label lblSmoot;
        private System.Windows.Forms.Button btnSmoothDown;
        private System.Windows.Forms.Button btnSmoothUp;
        private System.Windows.Forms.Button btnSmoothApply;
        private System.Windows.Forms.Label lblSmoothVal;
        private System.Windows.Forms.Button btnPrevLine;
        private System.Windows.Forms.Button btnNextLine;
        private System.Windows.Forms.Button btnDeleteLine;
        private System.Windows.Forms.Button btnCancel;
        private System.Windows.Forms.Button btnAccept;
        private System.Windows.Forms.Button btnNew;
        private System.Windows.Forms.Button btnEdit;
    }
}
