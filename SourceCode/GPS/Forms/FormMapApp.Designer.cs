namespace AgOpenGPS
{
    partial class FormMapApp
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
            this.chkBackgroundOn = new System.Windows.Forms.CheckBox();
            this.grpSource = new System.Windows.Forms.GroupBox();
            this.rbtnDefault = new System.Windows.Forms.RadioButton();
            this.rbtnGoogle = new System.Windows.Forms.RadioButton();
            this.rbtnCustom = new System.Windows.Forms.RadioButton();
            this.lblUrl = new System.Windows.Forms.Label();
            this.tboxWmsUrl = new System.Windows.Forms.TextBox();
            this.gMapControl = new GMap.NET.WindowsForms.GMapControl();
            this.lblStatus = new System.Windows.Forms.Label();
            this.btnClose = new System.Windows.Forms.Button();
            this.chkParcels = new System.Windows.Forms.CheckBox();
            this.btnParcelRefresh = new System.Windows.Forms.Button();
            this.lblParcelUrl = new System.Windows.Forms.Label();
            this.tboxParcelUrl = new System.Windows.Forms.TextBox();
            this.btnWinFull = new System.Windows.Forms.Button();
            this.btnWinClose = new System.Windows.Forms.Button();
            this.btnZoomIn = new System.Windows.Forms.Button();
            this.btnZoomOut = new System.Windows.Forms.Button();
            this.lblQuality = new System.Windows.Forms.Label();
            this.btnQuality = new System.Windows.Forms.Button();
            this.btnRecapture = new System.Windows.Forms.Button();
            this.grpSource.SuspendLayout();
            this.SuspendLayout();
            //
            // chkBackgroundOn
            //
            this.chkBackgroundOn.Appearance = System.Windows.Forms.Appearance.Button;
            this.chkBackgroundOn.BackColor = System.Drawing.Color.AliceBlue;
            this.chkBackgroundOn.FlatAppearance.BorderColor = System.Drawing.Color.Black;
            this.chkBackgroundOn.FlatAppearance.CheckedBackColor = System.Drawing.Color.MediumAquamarine;
            this.chkBackgroundOn.FlatStyle = System.Windows.Forms.FlatStyle.Flat;
            this.chkBackgroundOn.Font = new System.Drawing.Font("Tahoma", 14.25F, System.Drawing.FontStyle.Bold);
            this.chkBackgroundOn.Location = new System.Drawing.Point(16, 52);
            this.chkBackgroundOn.Name = "chkBackgroundOn";
            this.chkBackgroundOn.Size = new System.Drawing.Size(220, 80);
            this.chkBackgroundOn.TabIndex = 0;
            this.chkBackgroundOn.Text = "Background Map: Off";
            this.chkBackgroundOn.TextAlign = System.Drawing.ContentAlignment.MiddleCenter;
            this.chkBackgroundOn.UseVisualStyleBackColor = false;
            this.chkBackgroundOn.CheckedChanged += new System.EventHandler(this.chkBackgroundOn_CheckedChanged);
            //
            // grpSource
            //
            this.grpSource.Controls.Add(this.rbtnDefault);
            this.grpSource.Controls.Add(this.rbtnGoogle);
            this.grpSource.Controls.Add(this.rbtnCustom);
            this.grpSource.Font = new System.Drawing.Font("Tahoma", 11.25F, System.Drawing.FontStyle.Bold);
            this.grpSource.Location = new System.Drawing.Point(252, 52);
            this.grpSource.Name = "grpSource";
            this.grpSource.Size = new System.Drawing.Size(456, 80);
            this.grpSource.TabIndex = 1;
            this.grpSource.TabStop = false;
            this.grpSource.Text = "Map source";
            //
            // rbtnDefault
            //
            this.rbtnDefault.AutoSize = false;
            this.rbtnDefault.Font = new System.Drawing.Font("Tahoma", 11.25F);
            this.rbtnDefault.Location = new System.Drawing.Point(16, 26);
            this.rbtnDefault.Name = "rbtnDefault";
            this.rbtnDefault.Size = new System.Drawing.Size(104, 46);
            this.rbtnDefault.TabIndex = 0;
            this.rbtnDefault.Text = "Default";
            this.rbtnDefault.UseVisualStyleBackColor = true;
            this.rbtnDefault.CheckedChanged += new System.EventHandler(this.source_CheckedChanged);
            //
            // rbtnGoogle
            //
            this.rbtnGoogle.AutoSize = true;
            this.rbtnGoogle.Font = new System.Drawing.Font("Tahoma", 11.25F);
            this.rbtnGoogle.Location = new System.Drawing.Point(140, 38);
            this.rbtnGoogle.Name = "rbtnGoogle";
            this.rbtnGoogle.Size = new System.Drawing.Size(80, 22);
            this.rbtnGoogle.TabIndex = 1;
            this.rbtnGoogle.Text = "Google";
            this.rbtnGoogle.UseVisualStyleBackColor = true;
            this.rbtnGoogle.CheckedChanged += new System.EventHandler(this.source_CheckedChanged);
            //
            // rbtnCustom
            //
            this.rbtnCustom.AutoSize = true;
            this.rbtnCustom.Font = new System.Drawing.Font("Tahoma", 11.25F);
            this.rbtnCustom.Location = new System.Drawing.Point(258, 38);
            this.rbtnCustom.Name = "rbtnCustom";
            this.rbtnCustom.Size = new System.Drawing.Size(180, 22);
            this.rbtnCustom.TabIndex = 2;
            this.rbtnCustom.Text = "Custom WMS (DGU)";
            this.rbtnCustom.UseVisualStyleBackColor = true;
            this.rbtnCustom.CheckedChanged += new System.EventHandler(this.source_CheckedChanged);
            //
            // lblUrl
            //
            this.lblUrl.AutoSize = true;
            this.lblUrl.Font = new System.Drawing.Font("Tahoma", 11.25F);
            this.lblUrl.Location = new System.Drawing.Point(16, 144);
            this.lblUrl.Name = "lblUrl";
            this.lblUrl.Size = new System.Drawing.Size(82, 18);
            this.lblUrl.TabIndex = 2;
            this.lblUrl.Text = "WMS URL:";
            this.lblUrl.Visible = false;
            //
            // tboxWmsUrl
            //
            this.tboxWmsUrl.Font = new System.Drawing.Font("Tahoma", 10F);
            this.tboxWmsUrl.Location = new System.Drawing.Point(104, 141);
            this.tboxWmsUrl.Name = "tboxWmsUrl";
            this.tboxWmsUrl.Size = new System.Drawing.Size(604, 26);
            this.tboxWmsUrl.TabIndex = 3;
            this.tboxWmsUrl.Visible = false;
            this.tboxWmsUrl.Leave += new System.EventHandler(this.tboxWmsUrl_Leave);
            //
            // chkParcels
            //
            this.chkParcels.Appearance = System.Windows.Forms.Appearance.Button;
            this.chkParcels.BackColor = System.Drawing.Color.AliceBlue;
            this.chkParcels.FlatAppearance.BorderColor = System.Drawing.Color.Black;
            this.chkParcels.FlatAppearance.CheckedBackColor = System.Drawing.Color.MediumAquamarine;
            this.chkParcels.FlatStyle = System.Windows.Forms.FlatStyle.Flat;
            this.chkParcels.Font = new System.Drawing.Font("Tahoma", 10F, System.Drawing.FontStyle.Bold);
            this.chkParcels.Location = new System.Drawing.Point(16, 144);
            this.chkParcels.Name = "chkParcels";
            this.chkParcels.Size = new System.Drawing.Size(130, 36);
            this.chkParcels.TabIndex = 6;
            this.chkParcels.Text = "Parcel: Off";
            this.chkParcels.TextAlign = System.Drawing.ContentAlignment.MiddleCenter;
            this.chkParcels.UseVisualStyleBackColor = false;
            this.chkParcels.CheckedChanged += new System.EventHandler(this.chkParcels_CheckedChanged);
            //
            // btnParcelRefresh
            //
            this.btnParcelRefresh.Font = new System.Drawing.Font("Tahoma", 14F, System.Drawing.FontStyle.Bold);
            this.btnParcelRefresh.Location = new System.Drawing.Point(150, 144);
            this.btnParcelRefresh.Name = "btnParcelRefresh";
            this.btnParcelRefresh.Size = new System.Drawing.Size(40, 36);
            this.btnParcelRefresh.TabIndex = 7;
            this.btnParcelRefresh.Text = "⟳";
            this.btnParcelRefresh.UseVisualStyleBackColor = true;
            this.btnParcelRefresh.Click += new System.EventHandler(this.btnParcelRefresh_Click);
            //
            // lblQuality
            //
            this.lblQuality.AutoSize = true;
            this.lblQuality.Font = new System.Drawing.Font("Tahoma", 11.25F, System.Drawing.FontStyle.Bold);
            this.lblQuality.Location = new System.Drawing.Point(330, 153);
            this.lblQuality.Name = "lblQuality";
            this.lblQuality.Size = new System.Drawing.Size(90, 18);
            this.lblQuality.TabIndex = 10;
            this.lblQuality.Text = "Resolution:";
            //
            // btnQuality
            //
            this.btnQuality.BackColor = System.Drawing.Color.AliceBlue;
            this.btnQuality.FlatAppearance.BorderColor = System.Drawing.Color.Black;
            this.btnQuality.FlatStyle = System.Windows.Forms.FlatStyle.Flat;
            this.btnQuality.Font = new System.Drawing.Font("Tahoma", 10F, System.Drawing.FontStyle.Bold);
            this.btnQuality.Location = new System.Drawing.Point(450, 144);
            this.btnQuality.Name = "btnQuality";
            this.btnQuality.Size = new System.Drawing.Size(150, 36);
            this.btnQuality.TabIndex = 11;
            this.btnQuality.Text = "Low";
            this.btnQuality.UseVisualStyleBackColor = false;
            this.btnQuality.Click += new System.EventHandler(this.btnQuality_Click);
            //
            // btnRecapture
            //
            this.btnRecapture.Font = new System.Drawing.Font("Tahoma", 14F, System.Drawing.FontStyle.Bold);
            this.btnRecapture.Location = new System.Drawing.Point(610, 144);
            this.btnRecapture.Name = "btnRecapture";
            this.btnRecapture.Size = new System.Drawing.Size(40, 36);
            this.btnRecapture.TabIndex = 12;
            this.btnRecapture.Text = "⟳";
            this.btnRecapture.UseVisualStyleBackColor = true;
            this.btnRecapture.Click += new System.EventHandler(this.btnRecapture_Click);
            //
            // lblParcelUrl
            //
            this.lblParcelUrl.AutoSize = true;
            this.lblParcelUrl.Font = new System.Drawing.Font("Tahoma", 11.25F);
            this.lblParcelUrl.Location = new System.Drawing.Point(200, 182);
            this.lblParcelUrl.Name = "lblParcelUrl";
            this.lblParcelUrl.Size = new System.Drawing.Size(112, 18);
            this.lblParcelUrl.TabIndex = 8;
            this.lblParcelUrl.Text = "Parcel WFS URL:";
            this.lblParcelUrl.Visible = false;
            //
            // tboxParcelUrl
            //
            this.tboxParcelUrl.Font = new System.Drawing.Font("Tahoma", 10F);
            this.tboxParcelUrl.Location = new System.Drawing.Point(318, 179);
            this.tboxParcelUrl.Name = "tboxParcelUrl";
            this.tboxParcelUrl.Size = new System.Drawing.Size(390, 26);
            this.tboxParcelUrl.TabIndex = 9;
            this.tboxParcelUrl.Visible = false;
            this.tboxParcelUrl.Leave += new System.EventHandler(this.tboxParcelUrl_Leave);
            //
            // gMapControl
            //
            this.gMapControl.Anchor = ((System.Windows.Forms.AnchorStyles)((((System.Windows.Forms.AnchorStyles.Top | System.Windows.Forms.AnchorStyles.Bottom)
                        | System.Windows.Forms.AnchorStyles.Left)
                        | System.Windows.Forms.AnchorStyles.Right)));
            this.gMapControl.Bearing = 0F;
            this.gMapControl.CanDragMap = true;
            this.gMapControl.GrayScaleMode = false;
            this.gMapControl.Location = new System.Drawing.Point(16, 186);
            this.gMapControl.MarkersEnabled = true;
            this.gMapControl.MaxZoom = 20;
            this.gMapControl.MinZoom = 2;
            this.gMapControl.MouseWheelZoomType = GMap.NET.MouseWheelZoomType.MousePositionAndCenter;
            this.gMapControl.Name = "gMapControl";
            this.gMapControl.NegativeMode = false;
            this.gMapControl.PolygonsEnabled = true;
            this.gMapControl.RoutesEnabled = true;
            this.gMapControl.ShowTileGridLines = false;
            this.gMapControl.Size = new System.Drawing.Size(692, 388);
            this.gMapControl.TabIndex = 4;
            this.gMapControl.Zoom = 0D;
            //
            // lblStatus
            //
            this.lblStatus.AutoSize = true;
            this.lblStatus.Anchor = ((System.Windows.Forms.AnchorStyles)((System.Windows.Forms.AnchorStyles.Bottom | System.Windows.Forms.AnchorStyles.Left)));
            this.lblStatus.Font = new System.Drawing.Font("Tahoma", 11.25F, System.Drawing.FontStyle.Bold);
            this.lblStatus.ForeColor = System.Drawing.Color.DimGray;
            this.lblStatus.Location = new System.Drawing.Point(16, 588);
            this.lblStatus.Name = "lblStatus";
            this.lblStatus.Size = new System.Drawing.Size(0, 18);
            this.lblStatus.TabIndex = 5;
            //
            // btnClose
            //
            this.btnClose.Anchor = ((System.Windows.Forms.AnchorStyles)((System.Windows.Forms.AnchorStyles.Bottom | System.Windows.Forms.AnchorStyles.Right)));
            this.btnClose.Font = new System.Drawing.Font("Tahoma", 11.25F, System.Drawing.FontStyle.Bold);
            this.btnClose.Location = new System.Drawing.Point(548, 580);
            this.btnClose.Name = "btnClose";
            this.btnClose.Size = new System.Drawing.Size(160, 44);
            this.btnClose.TabIndex = 6;
            this.btnClose.Text = "Apply and Close";
            this.btnClose.UseVisualStyleBackColor = true;
            this.btnClose.Click += new System.EventHandler(this.btnClose_Click);
            //
            // btnWinFull
            //
            this.btnWinFull.Anchor = ((System.Windows.Forms.AnchorStyles)((System.Windows.Forms.AnchorStyles.Top | System.Windows.Forms.AnchorStyles.Right)));
            this.btnWinFull.BackColor = System.Drawing.Color.Transparent;
            this.btnWinFull.BackgroundImage = global::AgOpenGPS.Properties.Resources.WindowMaximize;
            this.btnWinFull.BackgroundImageLayout = System.Windows.Forms.ImageLayout.Zoom;
            this.btnWinFull.FlatAppearance.BorderSize = 0;
            this.btnWinFull.FlatStyle = System.Windows.Forms.FlatStyle.Flat;
            this.btnWinFull.Location = new System.Drawing.Point(600, 4);
            this.btnWinFull.Name = "btnWinFull";
            this.btnWinFull.Size = new System.Drawing.Size(56, 40);
            this.btnWinFull.TabIndex = 10;
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
            this.btnWinClose.Location = new System.Drawing.Point(662, 4);
            this.btnWinClose.Name = "btnWinClose";
            this.btnWinClose.Size = new System.Drawing.Size(56, 40);
            this.btnWinClose.TabIndex = 11;
            this.btnWinClose.UseVisualStyleBackColor = false;
            this.btnWinClose.Click += new System.EventHandler(this.btnWinClose_Click);
            //
            // btnZoomIn
            //
            this.btnZoomIn.Anchor = ((System.Windows.Forms.AnchorStyles)((System.Windows.Forms.AnchorStyles.Top | System.Windows.Forms.AnchorStyles.Right)));
            this.btnZoomIn.BackColor = System.Drawing.Color.White;
            this.btnZoomIn.Font = new System.Drawing.Font("Tahoma", 20F, System.Drawing.FontStyle.Bold);
            this.btnZoomIn.Location = new System.Drawing.Point(644, 196);
            this.btnZoomIn.Name = "btnZoomIn";
            this.btnZoomIn.Size = new System.Drawing.Size(58, 58);
            this.btnZoomIn.TabIndex = 12;
            this.btnZoomIn.Text = "+";
            this.btnZoomIn.UseVisualStyleBackColor = false;
            this.btnZoomIn.Click += new System.EventHandler(this.btnZoomIn_Click);
            //
            // btnZoomOut
            //
            this.btnZoomOut.Anchor = ((System.Windows.Forms.AnchorStyles)((System.Windows.Forms.AnchorStyles.Top | System.Windows.Forms.AnchorStyles.Right)));
            this.btnZoomOut.BackColor = System.Drawing.Color.White;
            this.btnZoomOut.Font = new System.Drawing.Font("Tahoma", 20F, System.Drawing.FontStyle.Bold);
            this.btnZoomOut.Location = new System.Drawing.Point(644, 260);
            this.btnZoomOut.Name = "btnZoomOut";
            this.btnZoomOut.Size = new System.Drawing.Size(58, 58);
            this.btnZoomOut.TabIndex = 13;
            this.btnZoomOut.Text = "−";
            this.btnZoomOut.UseVisualStyleBackColor = false;
            this.btnZoomOut.Click += new System.EventHandler(this.btnZoomOut_Click);
            //
            // FormMapApp
            //
            this.AcceptButton = this.btnClose;
            this.AutoScaleDimensions = new System.Drawing.SizeF(6F, 13F);
            this.AutoScaleMode = System.Windows.Forms.AutoScaleMode.Font;
            this.BackColor = System.Drawing.Color.WhiteSmoke;
            this.ClientSize = new System.Drawing.Size(724, 636);
            this.Controls.Add(this.btnWinFull);
            this.Controls.Add(this.btnWinClose);
            this.Controls.Add(this.btnZoomIn);
            this.Controls.Add(this.btnZoomOut);
            this.Controls.Add(this.btnClose);
            this.Controls.Add(this.lblStatus);
            this.Controls.Add(this.gMapControl);
            this.Controls.Add(this.tboxParcelUrl);
            this.Controls.Add(this.lblParcelUrl);
            this.Controls.Add(this.btnRecapture);
            this.Controls.Add(this.btnQuality);
            this.Controls.Add(this.lblQuality);
            this.Controls.Add(this.btnParcelRefresh);
            this.Controls.Add(this.chkParcels);
            this.Controls.Add(this.tboxWmsUrl);
            this.Controls.Add(this.lblUrl);
            this.Controls.Add(this.grpSource);
            this.Controls.Add(this.chkBackgroundOn);
            this.Font = new System.Drawing.Font("Tahoma", 8.25F);
            this.FormBorderStyle = System.Windows.Forms.FormBorderStyle.None;
            this.MinimumSize = new System.Drawing.Size(560, 460);
            this.Name = "FormMapApp";
            this.StartPosition = System.Windows.Forms.FormStartPosition.CenterParent;
            this.Text = "Map App";
            this.Load += new System.EventHandler(this.FormMapApp_Load);
            this.FormClosing += new System.Windows.Forms.FormClosingEventHandler(this.FormMapApp_FormClosing);
            this.grpSource.ResumeLayout(false);
            this.grpSource.PerformLayout();
            this.ResumeLayout(false);
            this.PerformLayout();
        }

        #endregion

        private System.Windows.Forms.CheckBox chkBackgroundOn;
        private System.Windows.Forms.CheckBox chkParcels;
        private System.Windows.Forms.Button btnParcelRefresh;
        private System.Windows.Forms.Label lblParcelUrl;
        private System.Windows.Forms.TextBox tboxParcelUrl;
        private System.Windows.Forms.GroupBox grpSource;
        private System.Windows.Forms.RadioButton rbtnDefault;
        private System.Windows.Forms.RadioButton rbtnGoogle;
        private System.Windows.Forms.RadioButton rbtnCustom;
        private System.Windows.Forms.Label lblUrl;
        private System.Windows.Forms.TextBox tboxWmsUrl;
        private GMap.NET.WindowsForms.GMapControl gMapControl;
        private System.Windows.Forms.Label lblStatus;
        private System.Windows.Forms.Button btnClose;
        private System.Windows.Forms.Button btnWinFull;
        private System.Windows.Forms.Button btnWinClose;
        private System.Windows.Forms.Button btnZoomIn;
        private System.Windows.Forms.Button btnZoomOut;
        private System.Windows.Forms.Label lblQuality;
        private System.Windows.Forms.Button btnQuality;
        private System.Windows.Forms.Button btnRecapture;
    }
}
