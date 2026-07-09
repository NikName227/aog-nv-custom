namespace AgOpenGPS
{
    partial class FormGeoportalBoundary
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
            this.chkParcels = new System.Windows.Forms.CheckBox();
            this.btnRefresh = new System.Windows.Forms.Button();
            this.btnGoToPos = new System.Windows.Forms.Button();
            this.chkMarkParcel = new System.Windows.Forms.CheckBox();
            this.btnAddBridge = new System.Windows.Forms.Button();
            this.btnClearSel = new System.Windows.Forms.Button();
            this.btnMakeBoundary = new System.Windows.Forms.Button();
            this.btnSaveBoundary = new System.Windows.Forms.Button();
            this.btnEditBoundary = new System.Windows.Forms.Button();
            this.btnAddPoint = new System.Windows.Forms.CheckBox();
            this.btnDeletePoint = new System.Windows.Forms.CheckBox();
            this.btnUndo = new System.Windows.Forms.Button();
            this.btnAddObstacle = new System.Windows.Forms.Button();
            this.btnDeleteObstacle = new System.Windows.Forms.CheckBox();
            this.lblPointGroup = new System.Windows.Forms.Label();
            this.lblObstacleGroup = new System.Windows.Forms.Label();
            this.lblBoundaryGroup = new System.Windows.Forms.Label();
            this.lblSelInfo = new System.Windows.Forms.Label();
            this.gMapControl = new GMap.NET.WindowsForms.GMapControl();
            this.lblStatus = new System.Windows.Forms.Label();
            this.btnZoomIn = new System.Windows.Forms.Button();
            this.btnZoomOut = new System.Windows.Forms.Button();
            this.btnNavUp = new System.Windows.Forms.Button();
            this.btnNavDown = new System.Windows.Forms.Button();
            this.btnNavLeft = new System.Windows.Forms.Button();
            this.btnNavRight = new System.Windows.Forms.Button();
            this.btnPoint = new System.Windows.Forms.Button();
            this.lblPointMode = new System.Windows.Forms.Label();
            this.btnFine = new System.Windows.Forms.Button();
            this.btnCoarse = new System.Windows.Forms.Button();
            this.btnWinFull = new System.Windows.Forms.Button();
            this.btnWinClose = new System.Windows.Forms.Button();
            this.SuspendLayout();
            //
            // chkParcels
            //
            this.chkParcels.Appearance = System.Windows.Forms.Appearance.Button;
            this.chkParcels.BackColor = System.Drawing.Color.AliceBlue;
            this.chkParcels.Checked = true;
            this.chkParcels.CheckState = System.Windows.Forms.CheckState.Checked;
            this.chkParcels.FlatAppearance.CheckedBackColor = System.Drawing.Color.MediumAquamarine;
            this.chkParcels.FlatStyle = System.Windows.Forms.FlatStyle.Flat;
            this.chkParcels.Font = new System.Drawing.Font("Tahoma", 11.25F, System.Drawing.FontStyle.Bold);
            this.chkParcels.Location = new System.Drawing.Point(12, 8);
            this.chkParcels.Name = "chkParcels";
            this.chkParcels.Size = new System.Drawing.Size(200, 44);
            this.chkParcels.TabIndex = 0;
            this.chkParcels.Text = "Cadastral parcels";
            this.chkParcels.TextAlign = System.Drawing.ContentAlignment.MiddleCenter;
            this.chkParcels.UseVisualStyleBackColor = false;
            this.chkParcels.CheckedChanged += new System.EventHandler(this.chkParcels_CheckedChanged);
            //
            // btnRefresh
            //
            this.btnRefresh.Font = new System.Drawing.Font("Tahoma", 11.25F, System.Drawing.FontStyle.Bold);
            this.btnRefresh.Location = new System.Drawing.Point(220, 8);
            this.btnRefresh.Name = "btnRefresh";
            this.btnRefresh.Size = new System.Drawing.Size(150, 44);
            this.btnRefresh.TabIndex = 1;
            this.btnRefresh.Text = "Refresh parcels";
            this.btnRefresh.UseVisualStyleBackColor = true;
            this.btnRefresh.Click += new System.EventHandler(this.btnRefresh_Click);
            //
            // btnGoToPos
            //
            this.btnGoToPos.Font = new System.Drawing.Font("Tahoma", 11.25F, System.Drawing.FontStyle.Bold);
            this.btnGoToPos.Location = new System.Drawing.Point(378, 8);
            this.btnGoToPos.Name = "btnGoToPos";
            this.btnGoToPos.Size = new System.Drawing.Size(140, 44);
            this.btnGoToPos.TabIndex = 2;
            this.btnGoToPos.Text = "Go to position";
            this.btnGoToPos.UseVisualStyleBackColor = true;
            this.btnGoToPos.Click += new System.EventHandler(this.btnGoToPos_Click);
            //
            // chkMarkParcel
            //
            this.chkMarkParcel.Appearance = System.Windows.Forms.Appearance.Button;
            this.chkMarkParcel.BackColor = System.Drawing.Color.AliceBlue;
            this.chkMarkParcel.FlatAppearance.CheckedBackColor = System.Drawing.Color.Gold;
            this.chkMarkParcel.FlatStyle = System.Windows.Forms.FlatStyle.Flat;
            this.chkMarkParcel.Font = new System.Drawing.Font("Tahoma", 11.25F, System.Drawing.FontStyle.Bold);
            this.chkMarkParcel.Location = new System.Drawing.Point(12, 58);
            this.chkMarkParcel.Name = "chkMarkParcel";
            this.chkMarkParcel.Size = new System.Drawing.Size(170, 44);
            this.chkMarkParcel.TabIndex = 3;
            this.chkMarkParcel.Text = "Mark parcel";
            this.chkMarkParcel.TextAlign = System.Drawing.ContentAlignment.MiddleCenter;
            this.chkMarkParcel.UseVisualStyleBackColor = false;
            this.chkMarkParcel.CheckedChanged += new System.EventHandler(this.chkMarkParcel_CheckedChanged);
            //
            // btnAddBridge
            //
            this.btnAddBridge.BackColor = System.Drawing.Color.PapayaWhip;
            this.btnAddBridge.FlatStyle = System.Windows.Forms.FlatStyle.Flat;
            this.btnAddBridge.Font = new System.Drawing.Font("Tahoma", 11.25F, System.Drawing.FontStyle.Bold);
            this.btnAddBridge.Location = new System.Drawing.Point(190, 58);
            this.btnAddBridge.Name = "btnAddBridge";
            this.btnAddBridge.Size = new System.Drawing.Size(150, 44);
            this.btnAddBridge.TabIndex = 4;
            this.btnAddBridge.Text = "Add bridge";
            this.btnAddBridge.UseVisualStyleBackColor = false;
            this.btnAddBridge.Click += new System.EventHandler(this.btnAddBridge_Click);
            //
            // btnClearSel
            //
            this.btnClearSel.Font = new System.Drawing.Font("Tahoma", 11.25F, System.Drawing.FontStyle.Bold);
            this.btnClearSel.Location = new System.Drawing.Point(348, 58);
            this.btnClearSel.Name = "btnClearSel";
            this.btnClearSel.Size = new System.Drawing.Size(170, 44);
            this.btnClearSel.TabIndex = 5;
            this.btnClearSel.Text = "Clear selection";
            this.btnClearSel.UseVisualStyleBackColor = true;
            this.btnClearSel.Click += new System.EventHandler(this.btnClearSel_Click);
            //
            // btnMakeBoundary
            //
            this.btnMakeBoundary.BackColor = System.Drawing.Color.Honeydew;
            this.btnMakeBoundary.FlatStyle = System.Windows.Forms.FlatStyle.Flat;
            this.btnMakeBoundary.Font = new System.Drawing.Font("Tahoma", 11.25F, System.Drawing.FontStyle.Bold);
            this.btnMakeBoundary.Location = new System.Drawing.Point(108, 158);
            this.btnMakeBoundary.Name = "btnMakeBoundary";
            this.btnMakeBoundary.Size = new System.Drawing.Size(90, 40);
            this.btnMakeBoundary.TabIndex = 6;
            this.btnMakeBoundary.Text = "Make";
            this.btnMakeBoundary.UseVisualStyleBackColor = false;
            this.btnMakeBoundary.Click += new System.EventHandler(this.btnMakeBoundary_Click);
            //
            // btnSaveBoundary
            //
            this.btnSaveBoundary.BackColor = System.Drawing.Color.Honeydew;
            this.btnSaveBoundary.Enabled = false;
            this.btnSaveBoundary.FlatStyle = System.Windows.Forms.FlatStyle.Flat;
            this.btnSaveBoundary.Font = new System.Drawing.Font("Tahoma", 11.25F, System.Drawing.FontStyle.Bold);
            this.btnSaveBoundary.Location = new System.Drawing.Point(202, 158);
            this.btnSaveBoundary.Name = "btnSaveBoundary";
            this.btnSaveBoundary.Size = new System.Drawing.Size(90, 40);
            this.btnSaveBoundary.TabIndex = 7;
            this.btnSaveBoundary.Text = "Save";
            this.btnSaveBoundary.UseVisualStyleBackColor = false;
            this.btnSaveBoundary.Click += new System.EventHandler(this.btnSaveBoundary_Click);
            //
            // btnEditBoundary
            //
            this.btnEditBoundary.BackColor = System.Drawing.Color.Honeydew;
            this.btnEditBoundary.Enabled = false;
            this.btnEditBoundary.FlatStyle = System.Windows.Forms.FlatStyle.Flat;
            this.btnEditBoundary.Font = new System.Drawing.Font("Tahoma", 11.25F, System.Drawing.FontStyle.Bold);
            this.btnEditBoundary.Location = new System.Drawing.Point(296, 158);
            this.btnEditBoundary.Name = "btnEditBoundary";
            this.btnEditBoundary.Size = new System.Drawing.Size(90, 40);
            this.btnEditBoundary.TabIndex = 22;
            this.btnEditBoundary.Text = "Edit";
            this.btnEditBoundary.UseVisualStyleBackColor = false;
            this.btnEditBoundary.Click += new System.EventHandler(this.btnEditBoundary_Click);
            //
            // btnAddPoint
            //
            this.btnAddPoint.Appearance = System.Windows.Forms.Appearance.Button;
            this.btnAddPoint.BackColor = System.Drawing.Color.AliceBlue;
            this.btnAddPoint.Enabled = false;
            this.btnAddPoint.FlatAppearance.CheckedBackColor = System.Drawing.Color.Gold;
            this.btnAddPoint.FlatStyle = System.Windows.Forms.FlatStyle.Flat;
            this.btnAddPoint.Font = new System.Drawing.Font("Tahoma", 10F, System.Drawing.FontStyle.Bold);
            this.btnAddPoint.Location = new System.Drawing.Point(78, 108);
            this.btnAddPoint.Name = "btnAddPoint";
            this.btnAddPoint.Size = new System.Drawing.Size(90, 40);
            this.btnAddPoint.TabIndex = 8;
            this.btnAddPoint.Text = "Add";
            this.btnAddPoint.TextAlign = System.Drawing.ContentAlignment.MiddleCenter;
            this.btnAddPoint.UseVisualStyleBackColor = false;
            this.btnAddPoint.CheckedChanged += new System.EventHandler(this.btnAddPoint_CheckedChanged);
            //
            // btnDeletePoint
            //
            this.btnDeletePoint.Appearance = System.Windows.Forms.Appearance.Button;
            this.btnDeletePoint.BackColor = System.Drawing.Color.AliceBlue;
            this.btnDeletePoint.Enabled = false;
            this.btnDeletePoint.FlatAppearance.CheckedBackColor = System.Drawing.Color.IndianRed;
            this.btnDeletePoint.FlatStyle = System.Windows.Forms.FlatStyle.Flat;
            this.btnDeletePoint.Font = new System.Drawing.Font("Tahoma", 10F, System.Drawing.FontStyle.Bold);
            this.btnDeletePoint.Location = new System.Drawing.Point(172, 108);
            this.btnDeletePoint.Name = "btnDeletePoint";
            this.btnDeletePoint.Size = new System.Drawing.Size(90, 40);
            this.btnDeletePoint.TabIndex = 9;
            this.btnDeletePoint.Text = "Delete";
            this.btnDeletePoint.TextAlign = System.Drawing.ContentAlignment.MiddleCenter;
            this.btnDeletePoint.UseVisualStyleBackColor = false;
            this.btnDeletePoint.CheckedChanged += new System.EventHandler(this.btnDeletePoint_CheckedChanged);
            //
            // btnUndo
            //
            this.btnUndo.Enabled = false;
            this.btnUndo.Font = new System.Drawing.Font("Tahoma", 10F, System.Drawing.FontStyle.Bold);
            this.btnUndo.Location = new System.Drawing.Point(266, 108);
            this.btnUndo.Name = "btnUndo";
            this.btnUndo.Size = new System.Drawing.Size(90, 40);
            this.btnUndo.TabIndex = 10;
            this.btnUndo.Text = "Undo";
            this.btnUndo.UseVisualStyleBackColor = true;
            this.btnUndo.Click += new System.EventHandler(this.btnUndo_Click);
            //
            // btnAddObstacle
            //
            this.btnAddObstacle.BackColor = System.Drawing.Color.PapayaWhip;
            this.btnAddObstacle.FlatStyle = System.Windows.Forms.FlatStyle.Flat;
            this.btnAddObstacle.Font = new System.Drawing.Font("Tahoma", 10F, System.Drawing.FontStyle.Bold);
            this.btnAddObstacle.Location = new System.Drawing.Point(518, 158);
            this.btnAddObstacle.Name = "btnAddObstacle";
            this.btnAddObstacle.Size = new System.Drawing.Size(90, 40);
            this.btnAddObstacle.TabIndex = 23;
            this.btnAddObstacle.Text = "Add";
            this.btnAddObstacle.UseVisualStyleBackColor = false;
            this.btnAddObstacle.Click += new System.EventHandler(this.btnAddObstacle_Click);
            //
            // btnDeleteObstacle
            //
            this.btnDeleteObstacle.Appearance = System.Windows.Forms.Appearance.Button;
            this.btnDeleteObstacle.BackColor = System.Drawing.Color.AliceBlue;
            this.btnDeleteObstacle.FlatAppearance.CheckedBackColor = System.Drawing.Color.IndianRed;
            this.btnDeleteObstacle.FlatStyle = System.Windows.Forms.FlatStyle.Flat;
            this.btnDeleteObstacle.Font = new System.Drawing.Font("Tahoma", 10F, System.Drawing.FontStyle.Bold);
            this.btnDeleteObstacle.Location = new System.Drawing.Point(612, 158);
            this.btnDeleteObstacle.Name = "btnDeleteObstacle";
            this.btnDeleteObstacle.Size = new System.Drawing.Size(90, 40);
            this.btnDeleteObstacle.TabIndex = 24;
            this.btnDeleteObstacle.Text = "Delete";
            this.btnDeleteObstacle.TextAlign = System.Drawing.ContentAlignment.MiddleCenter;
            this.btnDeleteObstacle.UseVisualStyleBackColor = false;
            this.btnDeleteObstacle.CheckedChanged += new System.EventHandler(this.btnDeleteObstacle_CheckedChanged);
            //
            // lblPointGroup
            //
            this.lblPointGroup.Font = new System.Drawing.Font("Tahoma", 11.25F, System.Drawing.FontStyle.Bold);
            this.lblPointGroup.Location = new System.Drawing.Point(12, 116);
            this.lblPointGroup.Name = "lblPointGroup";
            this.lblPointGroup.Size = new System.Drawing.Size(62, 24);
            this.lblPointGroup.TabIndex = 25;
            this.lblPointGroup.Text = "Point:";
            this.lblPointGroup.TextAlign = System.Drawing.ContentAlignment.MiddleLeft;
            //
            // lblObstacleGroup
            //
            this.lblObstacleGroup.Font = new System.Drawing.Font("Tahoma", 11.25F, System.Drawing.FontStyle.Bold);
            this.lblObstacleGroup.Location = new System.Drawing.Point(422, 166);
            this.lblObstacleGroup.Name = "lblObstacleGroup";
            this.lblObstacleGroup.Size = new System.Drawing.Size(92, 24);
            this.lblObstacleGroup.TabIndex = 26;
            this.lblObstacleGroup.Text = "Obstacle:";
            this.lblObstacleGroup.TextAlign = System.Drawing.ContentAlignment.MiddleLeft;
            //
            // lblBoundaryGroup
            //
            this.lblBoundaryGroup.Font = new System.Drawing.Font("Tahoma", 11.25F, System.Drawing.FontStyle.Bold);
            this.lblBoundaryGroup.Location = new System.Drawing.Point(12, 166);
            this.lblBoundaryGroup.Name = "lblBoundaryGroup";
            this.lblBoundaryGroup.Size = new System.Drawing.Size(92, 24);
            this.lblBoundaryGroup.TabIndex = 27;
            this.lblBoundaryGroup.Text = "Boundary:";
            this.lblBoundaryGroup.TextAlign = System.Drawing.ContentAlignment.MiddleLeft;
            //
            // lblSelInfo
            //
            this.lblSelInfo.Anchor = ((System.Windows.Forms.AnchorStyles)((System.Windows.Forms.AnchorStyles.Bottom | System.Windows.Forms.AnchorStyles.Right)));
            this.lblSelInfo.Font = new System.Drawing.Font("Tahoma", 11.25F, System.Drawing.FontStyle.Bold);
            this.lblSelInfo.ForeColor = System.Drawing.Color.DimGray;
            this.lblSelInfo.Location = new System.Drawing.Point(688, 674);
            this.lblSelInfo.Name = "lblSelInfo";
            this.lblSelInfo.Size = new System.Drawing.Size(340, 22);
            this.lblSelInfo.TabIndex = 8;
            this.lblSelInfo.TextAlign = System.Drawing.ContentAlignment.MiddleRight;
            //
            // gMapControl
            //
            this.gMapControl.Anchor = ((System.Windows.Forms.AnchorStyles)((((System.Windows.Forms.AnchorStyles.Top | System.Windows.Forms.AnchorStyles.Bottom)
                        | System.Windows.Forms.AnchorStyles.Left)
                        | System.Windows.Forms.AnchorStyles.Right)));
            this.gMapControl.Bearing = 0F;
            this.gMapControl.CanDragMap = true;
            this.gMapControl.GrayScaleMode = false;
            this.gMapControl.Location = new System.Drawing.Point(12, 204);
            this.gMapControl.MarkersEnabled = true;
            this.gMapControl.MaxZoom = 22;
            this.gMapControl.MinZoom = 2;
            this.gMapControl.MouseWheelZoomType = GMap.NET.MouseWheelZoomType.MousePositionAndCenter;
            this.gMapControl.Name = "gMapControl";
            this.gMapControl.NegativeMode = false;
            this.gMapControl.PolygonsEnabled = true;
            this.gMapControl.RoutesEnabled = true;
            this.gMapControl.ShowTileGridLines = false;
            this.gMapControl.Size = new System.Drawing.Size(1016, 468);
            this.gMapControl.TabIndex = 9;
            this.gMapControl.Zoom = 0D;
            //
            // lblStatus
            //
            this.lblStatus.Anchor = ((System.Windows.Forms.AnchorStyles)((System.Windows.Forms.AnchorStyles.Bottom | System.Windows.Forms.AnchorStyles.Left)));
            this.lblStatus.AutoSize = true;
            this.lblStatus.Font = new System.Drawing.Font("Tahoma", 11.25F, System.Drawing.FontStyle.Bold);
            this.lblStatus.ForeColor = System.Drawing.Color.DimGray;
            this.lblStatus.Location = new System.Drawing.Point(12, 678);
            this.lblStatus.Name = "lblStatus";
            this.lblStatus.Size = new System.Drawing.Size(0, 18);
            this.lblStatus.TabIndex = 10;
            //
            // btnZoomIn (on the map, top-right, stacked above btnZoomOut)
            //
            this.btnZoomIn.Anchor = ((System.Windows.Forms.AnchorStyles)((System.Windows.Forms.AnchorStyles.Top | System.Windows.Forms.AnchorStyles.Right)));
            this.btnZoomIn.BackColor = System.Drawing.Color.White;
            this.btnZoomIn.Font = new System.Drawing.Font("Tahoma", 20F, System.Drawing.FontStyle.Bold);
            this.btnZoomIn.Location = new System.Drawing.Point(960, 214);
            this.btnZoomIn.Name = "btnZoomIn";
            this.btnZoomIn.Size = new System.Drawing.Size(56, 56);
            this.btnZoomIn.TabIndex = 11;
            this.btnZoomIn.Text = "+";
            this.btnZoomIn.UseVisualStyleBackColor = false;
            this.btnZoomIn.Click += new System.EventHandler(this.btnZoomIn_Click);
            //
            // btnZoomOut
            //
            this.btnZoomOut.Anchor = ((System.Windows.Forms.AnchorStyles)((System.Windows.Forms.AnchorStyles.Top | System.Windows.Forms.AnchorStyles.Right)));
            this.btnZoomOut.BackColor = System.Drawing.Color.White;
            this.btnZoomOut.Font = new System.Drawing.Font("Tahoma", 20F, System.Drawing.FontStyle.Bold);
            this.btnZoomOut.Location = new System.Drawing.Point(960, 276);
            this.btnZoomOut.Name = "btnZoomOut";
            this.btnZoomOut.Size = new System.Drawing.Size(56, 56);
            this.btnZoomOut.TabIndex = 12;
            this.btnZoomOut.Text = "−";
            this.btnZoomOut.UseVisualStyleBackColor = false;
            this.btnZoomOut.Click += new System.EventHandler(this.btnZoomOut_Click);
            //
            // btnNavUp (top-right corner, above the map)
            //
            this.btnNavUp.Anchor = ((System.Windows.Forms.AnchorStyles)((System.Windows.Forms.AnchorStyles.Top | System.Windows.Forms.AnchorStyles.Right)));
            this.btnNavUp.FlatStyle = System.Windows.Forms.FlatStyle.Flat;
            this.btnNavUp.Font = new System.Drawing.Font("Tahoma", 20F, System.Drawing.FontStyle.Bold);
            this.btnNavUp.ForeColor = System.Drawing.Color.RoyalBlue;
            this.btnNavUp.Location = new System.Drawing.Point(798, 4);
            this.btnNavUp.Name = "btnNavUp";
            this.btnNavUp.Size = new System.Drawing.Size(56, 56);
            this.btnNavUp.TabIndex = 14;
            this.btnNavUp.Text = "▲";
            this.btnNavUp.UseVisualStyleBackColor = true;
            this.btnNavUp.Click += new System.EventHandler(this.btnNavUp_Click);
            //
            // btnFine (left of the arrow-pad)
            //
            this.btnFine.Anchor = ((System.Windows.Forms.AnchorStyles)((System.Windows.Forms.AnchorStyles.Top | System.Windows.Forms.AnchorStyles.Right)));
            this.btnFine.FlatStyle = System.Windows.Forms.FlatStyle.Flat;
            this.btnFine.Font = new System.Drawing.Font("Tahoma", 10F, System.Drawing.FontStyle.Bold);
            this.btnFine.Location = new System.Drawing.Point(654, 62);
            this.btnFine.Name = "btnFine";
            this.btnFine.Size = new System.Drawing.Size(80, 44);
            this.btnFine.TabIndex = 13;
            this.btnFine.Text = "Fine";
            this.btnFine.UseVisualStyleBackColor = true;
            this.btnFine.Click += new System.EventHandler(this.btnFine_Click);
            //
            // btnCoarse
            //
            this.btnCoarse.Anchor = ((System.Windows.Forms.AnchorStyles)((System.Windows.Forms.AnchorStyles.Top | System.Windows.Forms.AnchorStyles.Right)));
            this.btnCoarse.BackColor = System.Drawing.Color.LightSteelBlue;
            this.btnCoarse.FlatStyle = System.Windows.Forms.FlatStyle.Flat;
            this.btnCoarse.Font = new System.Drawing.Font("Tahoma", 10F, System.Drawing.FontStyle.Bold);
            this.btnCoarse.Location = new System.Drawing.Point(654, 110);
            this.btnCoarse.Name = "btnCoarse";
            this.btnCoarse.Size = new System.Drawing.Size(80, 44);
            this.btnCoarse.TabIndex = 13;
            this.btnCoarse.Text = "Coarse";
            this.btnCoarse.UseVisualStyleBackColor = false;
            this.btnCoarse.Click += new System.EventHandler(this.btnCoarse_Click);
            //
            // btnNavLeft
            //
            this.btnNavLeft.Anchor = ((System.Windows.Forms.AnchorStyles)((System.Windows.Forms.AnchorStyles.Top | System.Windows.Forms.AnchorStyles.Right)));
            this.btnNavLeft.FlatStyle = System.Windows.Forms.FlatStyle.Flat;
            this.btnNavLeft.Font = new System.Drawing.Font("Tahoma", 20F, System.Drawing.FontStyle.Bold);
            this.btnNavLeft.ForeColor = System.Drawing.Color.RoyalBlue;
            this.btnNavLeft.Location = new System.Drawing.Point(740, 62);
            this.btnNavLeft.Name = "btnNavLeft";
            this.btnNavLeft.Size = new System.Drawing.Size(56, 56);
            this.btnNavLeft.TabIndex = 15;
            this.btnNavLeft.Text = "◄";
            this.btnNavLeft.UseVisualStyleBackColor = true;
            this.btnNavLeft.Click += new System.EventHandler(this.btnNavLeft_Click);
            //
            // btnPoint
            //
            this.btnPoint.Anchor = ((System.Windows.Forms.AnchorStyles)((System.Windows.Forms.AnchorStyles.Top | System.Windows.Forms.AnchorStyles.Right)));
            this.btnPoint.BackColor = System.Drawing.Color.Gainsboro;
            this.btnPoint.FlatStyle = System.Windows.Forms.FlatStyle.Flat;
            this.btnPoint.Font = new System.Drawing.Font("Tahoma", 18F, System.Drawing.FontStyle.Bold);
            this.btnPoint.Location = new System.Drawing.Point(798, 62);
            this.btnPoint.Name = "btnPoint";
            this.btnPoint.Size = new System.Drawing.Size(56, 56);
            this.btnPoint.TabIndex = 16;
            this.btnPoint.Text = "●";
            this.btnPoint.UseVisualStyleBackColor = false;
            this.btnPoint.Click += new System.EventHandler(this.btnPoint_Click);
            //
            // btnNavRight
            //
            this.btnNavRight.Anchor = ((System.Windows.Forms.AnchorStyles)((System.Windows.Forms.AnchorStyles.Top | System.Windows.Forms.AnchorStyles.Right)));
            this.btnNavRight.FlatStyle = System.Windows.Forms.FlatStyle.Flat;
            this.btnNavRight.Font = new System.Drawing.Font("Tahoma", 20F, System.Drawing.FontStyle.Bold);
            this.btnNavRight.ForeColor = System.Drawing.Color.RoyalBlue;
            this.btnNavRight.Location = new System.Drawing.Point(856, 62);
            this.btnNavRight.Name = "btnNavRight";
            this.btnNavRight.Size = new System.Drawing.Size(56, 56);
            this.btnNavRight.TabIndex = 17;
            this.btnNavRight.Text = "►";
            this.btnNavRight.UseVisualStyleBackColor = true;
            this.btnNavRight.Click += new System.EventHandler(this.btnNavRight_Click);
            //
            // btnNavDown
            //
            this.btnNavDown.Anchor = ((System.Windows.Forms.AnchorStyles)((System.Windows.Forms.AnchorStyles.Top | System.Windows.Forms.AnchorStyles.Right)));
            this.btnNavDown.FlatStyle = System.Windows.Forms.FlatStyle.Flat;
            this.btnNavDown.Font = new System.Drawing.Font("Tahoma", 20F, System.Drawing.FontStyle.Bold);
            this.btnNavDown.ForeColor = System.Drawing.Color.RoyalBlue;
            this.btnNavDown.Location = new System.Drawing.Point(798, 120);
            this.btnNavDown.Name = "btnNavDown";
            this.btnNavDown.Size = new System.Drawing.Size(56, 56);
            this.btnNavDown.TabIndex = 18;
            this.btnNavDown.Text = "▼";
            this.btnNavDown.UseVisualStyleBackColor = true;
            this.btnNavDown.Click += new System.EventHandler(this.btnNavDown_Click);
            //
            // lblPointMode
            //
            this.lblPointMode.Anchor = ((System.Windows.Forms.AnchorStyles)((System.Windows.Forms.AnchorStyles.Top | System.Windows.Forms.AnchorStyles.Right)));
            this.lblPointMode.Font = new System.Drawing.Font("Tahoma", 11.25F, System.Drawing.FontStyle.Bold);
            this.lblPointMode.ForeColor = System.Drawing.Color.DimGray;
            this.lblPointMode.Location = new System.Drawing.Point(734, 176);
            this.lblPointMode.Name = "lblPointMode";
            this.lblPointMode.Size = new System.Drawing.Size(180, 26);
            this.lblPointMode.TabIndex = 19;
            this.lblPointMode.TextAlign = System.Drawing.ContentAlignment.MiddleCenter;
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
            this.btnWinFull.TabIndex = 20;
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
            this.btnWinClose.TabIndex = 21;
            this.btnWinClose.UseVisualStyleBackColor = false;
            this.btnWinClose.Click += new System.EventHandler(this.btnWinClose_Click);
            //
            // FormGeoportalBoundary
            //
            this.AutoScaleDimensions = new System.Drawing.SizeF(6F, 13F);
            this.AutoScaleMode = System.Windows.Forms.AutoScaleMode.Font;
            this.BackColor = System.Drawing.Color.WhiteSmoke;
            this.ClientSize = new System.Drawing.Size(1040, 700);
            this.Controls.Add(this.btnWinFull);
            this.Controls.Add(this.btnWinClose);
            this.Controls.Add(this.lblPointMode);
            this.Controls.Add(this.btnNavDown);
            this.Controls.Add(this.btnNavRight);
            this.Controls.Add(this.btnPoint);
            this.Controls.Add(this.btnNavLeft);
            this.Controls.Add(this.btnNavUp);
            this.Controls.Add(this.btnCoarse);
            this.Controls.Add(this.btnFine);
            this.Controls.Add(this.btnZoomIn);
            this.Controls.Add(this.btnZoomOut);
            this.Controls.Add(this.lblStatus);
            this.Controls.Add(this.lblSelInfo);
            this.Controls.Add(this.btnUndo);
            this.Controls.Add(this.btnDeletePoint);
            this.Controls.Add(this.btnAddPoint);
            this.Controls.Add(this.btnAddObstacle);
            this.Controls.Add(this.btnDeleteObstacle);
            this.Controls.Add(this.btnEditBoundary);
            this.Controls.Add(this.lblPointGroup);
            this.Controls.Add(this.lblObstacleGroup);
            this.Controls.Add(this.lblBoundaryGroup);
            this.Controls.Add(this.btnSaveBoundary);
            this.Controls.Add(this.btnMakeBoundary);
            this.Controls.Add(this.btnClearSel);
            this.Controls.Add(this.btnAddBridge);
            this.Controls.Add(this.chkMarkParcel);
            this.Controls.Add(this.btnGoToPos);
            this.Controls.Add(this.btnRefresh);
            this.Controls.Add(this.chkParcels);
            this.Controls.Add(this.gMapControl);
            this.Font = new System.Drawing.Font("Tahoma", 8.25F);
            this.FormBorderStyle = System.Windows.Forms.FormBorderStyle.None;
            this.MinimumSize = new System.Drawing.Size(820, 560);
            this.Name = "FormGeoportalBoundary";
            this.StartPosition = System.Windows.Forms.FormStartPosition.CenterParent;
            this.Text = "Boundary from Geoportal";
            this.Load += new System.EventHandler(this.FormGeoportalBoundary_Load);
            this.Shown += new System.EventHandler(this.FormGeoportalBoundary_Shown);
            this.FormClosing += new System.Windows.Forms.FormClosingEventHandler(this.FormGeoportalBoundary_FormClosing);
            this.ResumeLayout(false);
            this.PerformLayout();
        }

        #endregion

        private System.Windows.Forms.CheckBox chkParcels;
        private System.Windows.Forms.Button btnRefresh;
        private System.Windows.Forms.Button btnGoToPos;
        private System.Windows.Forms.CheckBox chkMarkParcel;
        private System.Windows.Forms.Button btnAddBridge;
        private System.Windows.Forms.Button btnClearSel;
        private System.Windows.Forms.Button btnMakeBoundary;
        private System.Windows.Forms.Button btnSaveBoundary;
        private System.Windows.Forms.Button btnEditBoundary;
        private System.Windows.Forms.CheckBox btnAddPoint;
        private System.Windows.Forms.CheckBox btnDeletePoint;
        private System.Windows.Forms.Button btnUndo;
        private System.Windows.Forms.Button btnAddObstacle;
        private System.Windows.Forms.CheckBox btnDeleteObstacle;
        private System.Windows.Forms.Label lblPointGroup;
        private System.Windows.Forms.Label lblObstacleGroup;
        private System.Windows.Forms.Label lblBoundaryGroup;
        private System.Windows.Forms.Label lblSelInfo;
        private GMap.NET.WindowsForms.GMapControl gMapControl;
        private System.Windows.Forms.Label lblStatus;
        private System.Windows.Forms.Button btnZoomIn;
        private System.Windows.Forms.Button btnZoomOut;
        private System.Windows.Forms.Button btnNavUp;
        private System.Windows.Forms.Button btnNavDown;
        private System.Windows.Forms.Button btnNavLeft;
        private System.Windows.Forms.Button btnNavRight;
        private System.Windows.Forms.Button btnPoint;
        private System.Windows.Forms.Label lblPointMode;
        private System.Windows.Forms.Button btnFine;
        private System.Windows.Forms.Button btnCoarse;
        private System.Windows.Forms.Button btnWinFull;
        private System.Windows.Forms.Button btnWinClose;
    }
}
