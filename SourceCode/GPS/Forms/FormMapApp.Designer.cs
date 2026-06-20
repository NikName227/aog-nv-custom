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
            this.chkBackgroundOn.Location = new System.Drawing.Point(16, 12);
            this.chkBackgroundOn.Name = "chkBackgroundOn";
            this.chkBackgroundOn.Size = new System.Drawing.Size(220, 80);
            this.chkBackgroundOn.TabIndex = 0;
            this.chkBackgroundOn.Text = "Background Map: OFF";
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
            this.grpSource.Location = new System.Drawing.Point(252, 12);
            this.grpSource.Name = "grpSource";
            this.grpSource.Size = new System.Drawing.Size(456, 80);
            this.grpSource.TabIndex = 1;
            this.grpSource.TabStop = false;
            this.grpSource.Text = "Map source";
            //
            // rbtnDefault
            //
            this.rbtnDefault.AutoSize = true;
            this.rbtnDefault.Font = new System.Drawing.Font("Tahoma", 11.25F);
            this.rbtnDefault.Location = new System.Drawing.Point(16, 38);
            this.rbtnDefault.Name = "rbtnDefault";
            this.rbtnDefault.Size = new System.Drawing.Size(86, 22);
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
            this.lblUrl.Location = new System.Drawing.Point(16, 104);
            this.lblUrl.Name = "lblUrl";
            this.lblUrl.Size = new System.Drawing.Size(82, 18);
            this.lblUrl.TabIndex = 2;
            this.lblUrl.Text = "WMS URL:";
            //
            // tboxWmsUrl
            //
            this.tboxWmsUrl.Font = new System.Drawing.Font("Tahoma", 10F);
            this.tboxWmsUrl.Location = new System.Drawing.Point(104, 101);
            this.tboxWmsUrl.Name = "tboxWmsUrl";
            this.tboxWmsUrl.Size = new System.Drawing.Size(604, 26);
            this.tboxWmsUrl.TabIndex = 3;
            this.tboxWmsUrl.Leave += new System.EventHandler(this.tboxWmsUrl_Leave);
            //
            // gMapControl
            //
            this.gMapControl.Anchor = ((System.Windows.Forms.AnchorStyles)((((System.Windows.Forms.AnchorStyles.Top | System.Windows.Forms.AnchorStyles.Bottom)
                        | System.Windows.Forms.AnchorStyles.Left)
                        | System.Windows.Forms.AnchorStyles.Right)));
            this.gMapControl.Bearing = 0F;
            this.gMapControl.CanDragMap = true;
            this.gMapControl.GrayScaleMode = false;
            this.gMapControl.Location = new System.Drawing.Point(16, 138);
            this.gMapControl.MarkersEnabled = true;
            this.gMapControl.MaxZoom = 20;
            this.gMapControl.MinZoom = 2;
            this.gMapControl.MouseWheelZoomType = GMap.NET.MouseWheelZoomType.MousePositionAndCenter;
            this.gMapControl.Name = "gMapControl";
            this.gMapControl.NegativeMode = false;
            this.gMapControl.PolygonsEnabled = true;
            this.gMapControl.RoutesEnabled = true;
            this.gMapControl.ShowTileGridLines = false;
            this.gMapControl.Size = new System.Drawing.Size(692, 396);
            this.gMapControl.TabIndex = 4;
            this.gMapControl.Zoom = 0D;
            //
            // lblStatus
            //
            this.lblStatus.AutoSize = true;
            this.lblStatus.Anchor = ((System.Windows.Forms.AnchorStyles)((System.Windows.Forms.AnchorStyles.Bottom | System.Windows.Forms.AnchorStyles.Left)));
            this.lblStatus.Font = new System.Drawing.Font("Tahoma", 11.25F, System.Drawing.FontStyle.Bold);
            this.lblStatus.ForeColor = System.Drawing.Color.DimGray;
            this.lblStatus.Location = new System.Drawing.Point(16, 548);
            this.lblStatus.Name = "lblStatus";
            this.lblStatus.Size = new System.Drawing.Size(0, 18);
            this.lblStatus.TabIndex = 5;
            //
            // btnClose
            //
            this.btnClose.Anchor = ((System.Windows.Forms.AnchorStyles)((System.Windows.Forms.AnchorStyles.Bottom | System.Windows.Forms.AnchorStyles.Right)));
            this.btnClose.Font = new System.Drawing.Font("Tahoma", 11.25F, System.Drawing.FontStyle.Bold);
            this.btnClose.Location = new System.Drawing.Point(548, 540);
            this.btnClose.Name = "btnClose";
            this.btnClose.Size = new System.Drawing.Size(160, 44);
            this.btnClose.TabIndex = 6;
            this.btnClose.Text = "Apply && Close";
            this.btnClose.UseVisualStyleBackColor = true;
            this.btnClose.Click += new System.EventHandler(this.btnClose_Click);
            //
            // FormMapApp
            //
            this.AcceptButton = this.btnClose;
            this.AutoScaleDimensions = new System.Drawing.SizeF(6F, 13F);
            this.AutoScaleMode = System.Windows.Forms.AutoScaleMode.Font;
            this.BackColor = System.Drawing.Color.WhiteSmoke;
            this.ClientSize = new System.Drawing.Size(724, 596);
            this.Controls.Add(this.btnClose);
            this.Controls.Add(this.lblStatus);
            this.Controls.Add(this.gMapControl);
            this.Controls.Add(this.tboxWmsUrl);
            this.Controls.Add(this.lblUrl);
            this.Controls.Add(this.grpSource);
            this.Controls.Add(this.chkBackgroundOn);
            this.Font = new System.Drawing.Font("Tahoma", 8.25F);
            this.MinimumSize = new System.Drawing.Size(560, 420);
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
        private System.Windows.Forms.GroupBox grpSource;
        private System.Windows.Forms.RadioButton rbtnDefault;
        private System.Windows.Forms.RadioButton rbtnGoogle;
        private System.Windows.Forms.RadioButton rbtnCustom;
        private System.Windows.Forms.Label lblUrl;
        private System.Windows.Forms.TextBox tboxWmsUrl;
        private GMap.NET.WindowsForms.GMapControl gMapControl;
        private System.Windows.Forms.Label lblStatus;
        private System.Windows.Forms.Button btnClose;
    }
}
