using System;
using System.Collections.Generic;
using System.Diagnostics.Eventing.Reader;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Windows.Forms;
using AgLibrary.Logging;
using AgOpenGPS.Controls;
using AgOpenGPS.Core.Models;
using AgOpenGPS.Core.Translations;
using AgOpenGPS.Forms;
using AgOpenGPS.Properties;

namespace AgOpenGPS
{
    public partial class FormConfig
    {
        private decimal[] sectionWidthArr = new decimal[16];
        private decimal[] sectionPositionArr = new decimal[17];

        private double defaultSectionWidth;

        private int numberOfSections;

        #region Config
        private void tabTConfig_Enter(object sender, EventArgs e)
        {
            lblInchCm2.Text = mf.unitsInCm.ToString();

            if (mf.vehicle.VehicleConfig.Type != VehicleType.Harvester)
            {
                pboxConfigHarvester.Visible = false;

                rbtnTBT.Visible = true;
                rbtnTrailing.Visible = true;
                rbtnFixedRear.Visible = true;
                rbtnFront.Visible = true;
                //NV F06: Orchard tool type only for unlocked installs
                rbtnOrchard.Visible = CFeatureLock.IsOn(CFeatureLock.FeatureOrchard);
                //SIM 0730: the checkbox stayed on this tab when the parameters moved to the
                //Hitch tab, so its caption is translated here (HR "Voćnjak")
                rbtnOrchard.Text = gStr.gsOrchard;

                //F50 (SIM 0730): the orchard panel follows the app language too
                LocalizeOrchardPanel();

                //F50: orchard settings ride on the implement profile
                cboOrchardPath.SelectedIndex = OrchardModeToRow(COrchard.ResolvePathMode());
                nudOrchardTree.Value = (decimal)Properties.ToolSettings.Default.setTool_orchardTreeWidth;
                nudOrchardPassCount.Value = NudClamp(nudOrchardPassCount,
                    Properties.ToolSettings.Default.setTool_orchardPassCount);
                chkOrchardOnePass.Checked = Properties.ToolSettings.Default.setTool_orchardOnePass;
                ApplyOrchardPathUI();

                //SIM 0730: orchard is a MODE, not a fifth implement type - the checkbox rides on
                //top of whatever type is selected, so the type stays whatever it was last set to
                rbtnOrchard.Checked = Properties.ToolSettings.Default.setTool_isToolOrchard
                    && CFeatureLock.IsOn(CFeatureLock.FeatureOrchard);

                if (Properties.ToolSettings.Default.setTool_isToolFront)
                {
                    rbtnTBT.Checked = false;
                    rbtnTrailing.Checked = false;
                    rbtnFixedRear.Checked = false;
                    rbtnFront.Checked = true;
                }
                else if (Properties.ToolSettings.Default.setTool_isToolTBT)
                {
                    rbtnTBT.Checked = true;
                    rbtnTrailing.Checked = false;
                    rbtnFixedRear.Checked = false;
                    rbtnFront.Checked = false;
                }
                else if (Properties.ToolSettings.Default.setTool_isToolTrailing)
                {
                    rbtnTBT.Checked = false;
                    rbtnTrailing.Checked = true;
                    rbtnFixedRear.Checked = false;
                    rbtnFront.Checked = false;
                }
                else if (Properties.ToolSettings.Default.setTool_isToolRearFixed)
                {
                    rbtnTBT.Checked = false;
                    rbtnTrailing.Checked = false;
                    rbtnFixedRear.Checked = true;
                    rbtnFront.Checked = false;
                }
            }
            else
            {
                pboxConfigHarvester.Visible = true;

                rbtnTBT.Visible = false;
                rbtnTrailing.Visible = false;
                rbtnFixedRear.Visible = false;
                rbtnFront.Visible = false;
                rbtnOrchard.Visible = false;
                pnlOrchardSet.Visible = false;
            }
        }

        //a settings file from another install can hold a value outside this control's
        //range - clamp instead of throwing the whole tab
        private static decimal NudClamp(NumericUpDown nud, decimal v)
        {
            if (v < nud.Minimum) return nud.Minimum;
            if (v > nud.Maximum) return nud.Maximum;
            return v;
        }

        //F50 (SIM 0729 t3): each field in the orchard panel belongs to ONE path mode, so
        //only that mode's fields are on screen - a planter offset means nothing when the
        //machine is driving between the rows, and vice versa. The visible rows are packed
        //from the top so there are no holes left by the hidden ones.
        //F50 (SIM 0730): touch input - the orchard fields open the on-screen keypad like every
        //other Settings field does (stock AOG pattern: NudlessNumericUpDown + ShowKeypad).
        private void nudOrchard_Click(object sender, EventArgs e)
        {
            ((NudlessNumericUpDown)sender).ShowKeypad(this);
        }

        //F50 (SIM 0730): the list reads one pass -> two pass -> multi pass -> planting, which is
        //not the order the modes were added in. The stored numbers must not move (they live in
        //setTool_orchardPathMode), so only the DISPLAY order is defined here and everything maps
        //through it - a future reshuffle is this one array.
        private static readonly int[] orchardPathOrder =
        {
            COrchard.PathOnePass, COrchard.PathTwoPass, COrchard.PathMultiPass, COrchard.PathOnRow
        };

        private static int OrchardModeToRow(int mode)
        {
            for (int i = 0; i < orchardPathOrder.Length; i++) if (orchardPathOrder[i] == mode) return i;
            return 0;
        }

        private static int OrchardRowToMode(int row)
            => (row >= 0 && row < orchardPathOrder.Length) ? orchardPathOrder[row] : COrchard.PathOnePass;

        private void cboOrchardPath_SelectedIndexChanged(object sender, EventArgs e) => ApplyOrchardPathUI();

        //F50 (SIM 0730): the panel was hard-coded English. The combo items are rebuilt rather
        //than translated in place so the indices stay 0/1/2 = one pass / multi pass / on row.
        private bool orchardPanelLocalized;

        private void LocalizeOrchardPanel()
        {
            if (orchardPanelLocalized) return;
            orchardPanelLocalized = true;

            //the panel lives on the Hitch tab under the picture and its value, so it is laid
            //out compactly: path mode on top, the mode's own rows below it
            lblOrchardPath.Location = new Point(20, 12);
            cboOrchardPath.Location = new Point(340, 8);
            lblOrchardPath.Text = gStr.gsOrchPathMode;
            cboOrchardPath.Items.Clear();
            foreach (int m in orchardPathOrder)
            {
                cboOrchardPath.Items.Add(
                    m == COrchard.PathOnePass ? gStr.gsOrchPathOnePass :
                    m == COrchard.PathTwoPass ? gStr.gsOrchPathTwoPass :
                    m == COrchard.PathMultiPass ? gStr.gsOrchPathMultiPass :
                                                  gStr.gsOrchPathOnRow);
            }

            //F50 fix 2026-08-01: the implement side is no longer picked here - it is read from the
            //Tool Offset submenu. The combo is gone and its label became the read-only echo of
            //that value, so the driver sees what the orchard rules are using and where to change it.
            cboOrchardMode.Visible = false;
            lblOrchardMode.Size = new Size(560, lblOrchardMode.Size.Height);

            //F50 2026-08-01: "centre pass first" is off this panel. It ordered the BUILD of the
            //passes (their numbering, and the sequence handed to the field plan) but the driver
            //picks the pass by steering onto it - OrchardPickNearestPass takes the nearest one and
            //never looks at that order - so on screen it did nothing. The stored value still feeds
            //the build; the sequencing itself gets designed in the turn plan.
            chkOrchardCenterFirst.Visible = false;

            lblOrchardTree.Text = gStr.gsOrchTreeWidth;
            lblOrchardPassCount.Text = gStr.gsOrchPassCount;
        }

        private void ApplyOrchardPathUI()
        {
            int mode = cboOrchardPath.SelectedIndex < 0
                ? COrchard.PathOnePass
                : OrchardRowToMode(cboOrchardPath.SelectedIndex);

            bool multi = mode == COrchard.PathMultiPass;
            bool onRow = mode == COrchard.PathOnRow;
            bool twoPass = mode == COrchard.PathTwoPass;
            //between the rows (one, two or many passes) the clearance to the tree line applies
            bool between = !onRow;

            //two and multi pass care which side the implement is mounted on - shown here, set in
            //Tool Offset. Not editable on this panel any more (F50 fix 2026-08-01).
            bool side = multi || twoPass;
            lblOrchardMode.Text = gStr.gsOrchImplementSide + ": " + OrchardSideText();
            //superseded by the path mode; kept only so old profiles still migrate
            chkOrchardOnePass.Visible = false;

            //SIM 0730: pack by the INTENDED visibility, never by Control.Visible - WinForms
            //reports a control inside a hidden panel as invisible, and this runs from
            //tabTConfig_Enter BEFORE the panel is shown. Reading it there left every row on its
            //designer position, which is why "Implement side" only appeared on a second pick.
            int y = 52;
            PackOrchardRow(side, lblOrchardMode, null, ref y);
            PackOrchardRow(multi, lblOrchardPassCount, nudOrchardPassCount, ref y, lblOrchardPassWarn);
            PackOrchardRow(between, lblOrchardTree, nudOrchardTree, ref y);
        }

        //F50 fix 2026-08-01: the implement side as the orchard rules see it - the SIGN of the tool
        //offset, exactly what the stock Tool Offset icons show (> 0 right, < 0 left, 0 neither).
        //Offset 0 is not a side implement at all, so it reads "centred" and no pass is restricted.
        private string OrchardSideText()
        {
            double off = mf.tool.offset;
            if (off == 0) return gStr.gsOrchWorkCenter;

            return (off > 0 ? gStr.gsOrchWorkRight : gStr.gsOrchWorkLeft)
                + "  (" + (Math.Abs(off) * mf.m2InchOrCm).ToString("N1") + " " + mf.unitsInCm + ")";
        }

        private static void PackOrchardRow(bool show, Control label, Control field, ref int y,
            Control extra = null)
        {
            label.Visible = show;
            if (field != null) field.Visible = show;
            if (extra != null) extra.Visible = show;
            if (!show) return;

            label.Location = new Point(label.Location.X, y + 4);
            if (field != null) field.Location = new Point(field.Location.X, y);
            if (extra != null) extra.Location = new Point(extra.Location.X, y + 4);
            y += 38;
        }

        //F50 (SIM 0730): 1 and 2 passes ARE the one-pass and two-pass modes, so they are refused
        //here - the value snaps back to 0 (auto), the box goes red and says what is allowed.
        private void nudOrchardPassCount_ValueChanged(object sender, EventArgs e)
        {
            if (nudOrchardPassCount.Value == 1 || nudOrchardPassCount.Value == 2)
            {
                nudOrchardPassCount.Value = 0;
                nudOrchardPassCount.BackColor = Color.LightCoral;
                lblOrchardPassWarn.Text = gStr.gsOrchPassCountMin;
            }
            else
            {
                nudOrchardPassCount.BackColor = Color.White;
                lblOrchardPassWarn.Text = "";
            }
        }

        //F50 (SIM 0730): the orchard checkbox no longer takes part in the implement TYPE - it
        //only says whether the orchard mode is on. Its settings live on the Hitch tab, next to
        //the hitch geometry they depend on.
        private void rbtnOrchard_CheckedChanged(object sender, EventArgs e)
        {
            ApplyHitchTabLayout();
        }

        private void rbtnOrchard_Click(object sender, EventArgs e) { }

        private void tabTConfig_Leave(object sender, EventArgs e)
        {
            if (mf.vehicle.VehicleConfig.Type != VehicleType.Harvester)
            {
                //F50: orchard settings are saved regardless of the chosen type
                Properties.ToolSettings.Default.setTool_isToolOrchard = rbtnOrchard.Checked;
                if (cboOrchardPath.SelectedIndex >= 0)
                    Properties.ToolSettings.Default.setTool_orchardPathMode =
                        OrchardRowToMode(cboOrchardPath.SelectedIndex);
                //setTool_orchardWorkMode is NOT written any more - the implement side comes from
                //setVehicle_toolOffset (F50 fix 2026-08-01). The old value stays in the profile so
                //an install that rolls back keeps what it had.
                Properties.ToolSettings.Default.setTool_orchardTreeWidth = (double)nudOrchardTree.Value;
                Properties.ToolSettings.Default.setTool_orchardPassCount = (int)nudOrchardPassCount.Value;
                Properties.ToolSettings.Default.setTool_orchardOnePass = chkOrchardOnePass.Checked;
                //setTool_orchardCenterFirst is NOT written any more - it left this panel, but the
                //stored value keeps ordering the pass BUILD, so it must not be overwritten by a
                //checkbox nobody can see (F50 2026-08-01).

                //SIM 0730: the orchard checkbox does NOT decide the implement type any more -
                //the last chosen type is kept and used (a front-mounted pole or planter included)
                if (rbtnFront.Checked)
                {
                    Properties.ToolSettings.Default.setTool_isToolFront = true;
                    Properties.ToolSettings.Default.setTool_isToolTBT = false;
                    Properties.ToolSettings.Default.setTool_isToolTrailing = false;
                    Properties.ToolSettings.Default.setTool_isToolRearFixed = false;
                }
                else if (rbtnTBT.Checked)
                {
                    Properties.ToolSettings.Default.setTool_isToolFront = false;
                    Properties.ToolSettings.Default.setTool_isToolTBT = true;
                    Properties.ToolSettings.Default.setTool_isToolTrailing = true;
                    Properties.ToolSettings.Default.setTool_isToolRearFixed = false;
                }
                else if (rbtnTrailing.Checked)
                {
                    Properties.ToolSettings.Default.setTool_isToolFront = false;
                    Properties.ToolSettings.Default.setTool_isToolTBT = false;
                    Properties.ToolSettings.Default.setTool_isToolTrailing = true;
                    Properties.ToolSettings.Default.setTool_isToolRearFixed = false;
                }
                else if (rbtnFixedRear.Checked)
                {
                    Properties.ToolSettings.Default.setTool_isToolFront = false;
                    Properties.ToolSettings.Default.setTool_isToolTBT = false;
                    Properties.ToolSettings.Default.setTool_isToolTrailing = false;
                    Properties.ToolSettings.Default.setTool_isToolRearFixed = true;
                }
            }
            else
            {
                Properties.ToolSettings.Default.setTool_isToolFront = true;
                Properties.ToolSettings.Default.setTool_isToolTBT = false;
                Properties.ToolSettings.Default.setTool_isToolTrailing = false;
                Properties.ToolSettings.Default.setTool_isToolRearFixed = false;
                Properties.ToolSettings.Default.setTool_isToolOrchard = false;
            }

            mf.tool.isToolRearFixed = Properties.ToolSettings.Default.setTool_isToolRearFixed;
            mf.tool.isToolTrailing = Properties.ToolSettings.Default.setTool_isToolTrailing;
            mf.tool.isToolTBT = Properties.ToolSettings.Default.setTool_isToolTBT;
            mf.tool.isToolFrontFixed = Properties.ToolSettings.Default.setTool_isToolFront;
            mf.tool.isToolOrchard = Properties.ToolSettings.Default.setTool_isToolOrchard;
            mf.tool.orchardPathMode = Properties.ToolSettings.Default.setTool_orchardPathMode;

            //mf.tool.hitchLength = (double)nudDrawbarLength.Value * mf.inchOrCm2m;
            if (Properties.ToolSettings.Default.setTool_isToolFront && mf.tool.hitchLength < 0)
                mf.tool.hitchLength *= -1;
            else if (!Properties.ToolSettings.Default.setTool_isToolFront && mf.tool.hitchLength > 0)
                mf.tool.hitchLength *= -1;
            Properties.ToolSettings.Default.setVehicle_hitchLength = mf.tool.hitchLength;

            Properties.VehicleSettings.Default.Save();
            Properties.ToolSettings.Default.Save();
        }

        #endregion

        #region  Hitch

        //F50 (SIM 0730): with orchard on, the hitch picture shrinks and the orchard settings sit
        //underneath it - the two belong together (the hitch geometry is what the orchard measures
        //from), and the implement-type page stays free of them.
        private void ApplyHitchTabLayout()
        {
            bool orch = rbtnOrchard.Checked && CFeatureLock.IsOn(CFeatureLock.FeatureOrchard);

            //SIM 0730 markup: shrink the picture in BOTH directions (height alone left it
            //stretched), and keep the hitch VALUE clear of the orchard rows - it sits right under
            //the picture, so the panel starts below it.
            picboxToolHitch.Location = orch ? new Point(210, 8) : new Point(13, 12);
            picboxToolHitch.Size = orch ? new Size(420, 204) : new Size(838, 407);

            int nudY = orch ? 218 : 407;
            nudDrawbarLength.Location = new Point(nudDrawbarLength.Location.X, nudY);
            nudTrailingHitchLength.Location = new Point(nudTrailingHitchLength.Location.X, nudY);
            nudTankHitch.Location = new Point(nudTankHitch.Location.X, nudY);
            labelTractorUnits.Location = new Point(labelTractorUnits.Location.X, nudY + 69);
            label112.Location = new Point(label112.Location.X, nudY + 88);

            pnlOrchardSet.Visible = orch;
            if (orch)
            {
                pnlOrchardSet.BringToFront();
                ApplyOrchardPathUI();
            }
        }

        private void tabTHitch_Enter(object sender, EventArgs e)
        {
            ApplyHitchTabLayout();
            if (mf.vehicle.VehicleConfig.Type != VehicleType.Harvester)
            {
                //fixed -hitch only on vehicle
                if (Properties.ToolSettings.Default.setTool_isToolFront)
                {
                    nudTrailingHitchLength.Visible = false;
                    nudDrawbarLength.Visible = true;
                    nudTankHitch.Visible = false;

                    nudDrawbarLength.Left = 401;

                    picboxToolHitch.BackgroundImage = Properties.Resources.ToolHitchPageFront;
                }
                else if (Properties.ToolSettings.Default.setTool_isToolRearFixed)
                {
                    nudTrailingHitchLength.Visible = false;
                    nudDrawbarLength.Visible = true;
                    nudTankHitch.Visible = false;

                    nudDrawbarLength.Left = 259;

                    picboxToolHitch.BackgroundImage = Properties.Resources.ToolHitchPageRear;
                }

                //trailing TBT
                else if (Properties.ToolSettings.Default.setTool_isToolTBT)
                {
                    nudTrailingHitchLength.Visible = true;
                    nudDrawbarLength.Visible = false;
                    nudTankHitch.Visible = true;

                    nudTrailingHitchLength.Left = 326;
                    nudTankHitch.Left = 643;

                    picboxToolHitch.BackgroundImage = Properties.Resources.ToolHitchPageTBT;
                }

                //normal trailing
                else if (Properties.ToolSettings.Default.setTool_isToolTrailing)
                {
                    nudTrailingHitchLength.Visible = true;
                    nudDrawbarLength.Visible = false;
                    nudTankHitch.Visible = false;

                    nudTrailingHitchLength.Left = 438;

                    picboxToolHitch.BackgroundImage = Properties.Resources.ToolHitchPageTrailing;
                }

                label112.Text = mf.unitsInCm;
            }
            else
            {
                nudTrailingHitchLength.Visible = false;
                nudDrawbarLength.Visible = true;
                nudTankHitch.Visible = false;

                nudDrawbarLength.Left = 580;

                picboxToolHitch.BackgroundImage = Properties.Resources.ToolHitchPageFrontHarvester;
                label112.Text = mf.unitsInCm;
            }

            nudDrawbarLength.Value = (int)(Math.Abs(Properties.ToolSettings.Default.setVehicle_hitchLength) * mf.m2InchOrCm);

            nudTrailingHitchLength.Value = (int)(Math.Abs(Properties.ToolSettings.Default.setVehicle_toolTrailingHitchLength) * mf.m2InchOrCm);
            nudTankHitch.Value = (int)(Math.Abs(Properties.ToolSettings.Default.setVehicle_tankTrailingHitchLength) * mf.m2InchOrCm);
        }

        private void tabTHitch_Leave(object sender, EventArgs e)
        {
            Properties.VehicleSettings.Default.Save();
            Properties.ToolSettings.Default.Save();
        }

        private void nudDrawbarLength_Click(object sender, EventArgs e)
        {
            if (((NudlessNumericUpDown)sender).ShowKeypad(this))
            {
                mf.tool.hitchLength = (double)nudDrawbarLength.Value * mf.inchOrCm2m;
                if (!Properties.ToolSettings.Default.setTool_isToolFront)
                {
                    mf.tool.hitchLength *= -1;
                }
                Properties.ToolSettings.Default.setVehicle_hitchLength = mf.tool.hitchLength;
            }
        }

        private void nudTankHitch_Click(object sender, EventArgs e)
        {
            if (((NudlessNumericUpDown)sender).ShowKeypad(this))
            {
                mf.tool.tankTrailingHitchLength = (double)nudTankHitch.Value * -mf.inchOrCm2m;
                Properties.ToolSettings.Default.setVehicle_tankTrailingHitchLength = mf.tool.tankTrailingHitchLength;
            }
        }

        private void nudTrailingHitchLength_Click(object sender, EventArgs e)
        {
            if (((NudlessNumericUpDown)sender).ShowKeypad(this))
            {
                mf.tool.trailingHitchLength = (double)nudTrailingHitchLength.Value * -mf.inchOrCm2m;
                Properties.ToolSettings.Default.setVehicle_toolTrailingHitchLength = mf.tool.trailingHitchLength;
            }
        }

        #endregion

        #region Settings

        private void tabTSettings_Enter(object sender, EventArgs e)
        {
            nudLookAhead.Value = (decimal)Properties.ToolSettings.Default.setVehicle_toolLookAheadOn;
            nudLookAheadOff.Value = (decimal)Properties.ToolSettings.Default.setVehicle_toolLookAheadOff;
            nudTurnOffDelay.Value = (decimal)Properties.ToolSettings.Default.setVehicle_toolOffDelay;
            //pictureBox3.Image = Resources.ToolLookaheadOn;
            //pictureBox4.Image = Resources.ToolLookaheadOff;
        }

        private void tabTSettings_Leave(object sender, EventArgs e)
        {

            Properties.ToolSettings.Default.setVehicle_toolLookAheadOn = mf.tool.lookAheadOnSetting;
            Properties.ToolSettings.Default.setVehicle_toolLookAheadOff = mf.tool.lookAheadOffSetting;
            Properties.ToolSettings.Default.setVehicle_toolOffDelay = mf.tool.turnOffDelay;

            ////line up manual buttons based on # of sections
            //mf.LineUpManualBtns();

            ////update the sections to newly configured widths and positions in main
            //mf.SectionSetPosition();

            ////update the widths of sections and tool width in main
            //mf.SectionCalcWidths();

            Properties.ToolSettings.Default.Save();
        }
        private void nudLookAhead_Click(object sender, EventArgs e)
        {
            if (((NudlessNumericUpDown)sender).ShowKeypad(this))
            {
                if (nudLookAheadOff.Value > (nudLookAhead.Value * 0.8m))
                {
                    nudLookAheadOff.Value = nudLookAhead.Value * 0.8m;
                }

                mf.tool.lookAheadOnSetting = (double)nudLookAhead.Value;
                mf.tool.lookAheadOffSetting = (double)nudLookAheadOff.Value;
                mf.tool.turnOffDelay = 0;
            }
        }

        private void nudLookAheadOff_Click(object sender, EventArgs e)
        {
            if (((NudlessNumericUpDown)sender).ShowKeypad(this))
            {
                if (nudLookAheadOff.Value > (nudLookAhead.Value * 0.8m))
                {
                    nudLookAheadOff.Value = nudLookAhead.Value * 0.8m;
                }
                mf.tool.lookAheadOffSetting = (double)nudLookAheadOff.Value;

                if (nudLookAheadOff.Value > 0)
                {
                    mf.tool.turnOffDelay = 0;
                    nudTurnOffDelay.Value = 0;
                }

                mf.tool.lookAheadOnSetting = (double)nudLookAhead.Value;
                mf.tool.lookAheadOffSetting = (double)nudLookAheadOff.Value;
                mf.tool.turnOffDelay = 0;


            }
        }

        private void nudTurnOffDelay_Click(object sender, EventArgs e)
        {
            if (((NudlessNumericUpDown)sender).ShowKeypad(this))
            {
                if (nudTurnOffDelay.Value > 0)
                {
                    nudLookAheadOff.Value = 0;
                }
                mf.tool.turnOffDelay = (double)nudTurnOffDelay.Value;
                mf.tool.lookAheadOffSetting = (double)nudLookAheadOff.Value;
                mf.tool.lookAheadOnSetting = (double)nudLookAhead.Value;
            }
        }

        #endregion

        #region offset
        private void tabToolOffset_Enter(object sender, EventArgs e)
        {
            nudOffset.Value = (decimal)(Math.Abs(Properties.ToolSettings.Default.setVehicle_toolOffset) * mf.m2InchOrCm);

            rbtnToolRightPositive.Checked = false;
            rbtnLeftNegative.Checked = false;
            rbtnToolRightPositive.Checked = Properties.ToolSettings.Default.setVehicle_toolOffset > 0;
            rbtnLeftNegative.Checked = Properties.ToolSettings.Default.setVehicle_toolOffset < 0;

            nudOverlap.Value = (decimal)(Math.Abs(Properties.ToolSettings.Default.setVehicle_toolOverlap) * mf.m2InchOrCm);

            rbtnToolOverlap.Checked = false;
            rbtnToolGap.Checked = false;
            rbtnToolOverlap.Checked = Properties.ToolSettings.Default.setVehicle_toolOverlap > 0;
            rbtnToolGap.Checked = Properties.ToolSettings.Default.setVehicle_toolOverlap < 0;

            label175.Text = mf.unitsInCm;
            label176.Text = mf.unitsInCm;
        }

        private void tabToolOffset_Leave(object sender, EventArgs e)
        {
            Properties.ToolSettings.Default.Save();
        }

        private void nudOffset_Click(object sender, EventArgs e)
        {
            if (((NudlessNumericUpDown)sender).ShowKeypad(this))
            {
                if (!rbtnToolRightPositive.Checked && !rbtnLeftNegative.Checked)
                    rbtnToolRightPositive.Checked = true;

                if (rbtnToolRightPositive.Checked)
                    mf.tool.offset = (double)nudOffset.Value * mf.inchOrCm2m;
                else
                    mf.tool.offset = (double)nudOffset.Value * -mf.inchOrCm2m;

                Properties.ToolSettings.Default.setVehicle_toolOffset = mf.tool.offset;
            }

            rbtnToolRightPositive.Checked = false;
            rbtnLeftNegative.Checked = false;
            rbtnToolRightPositive.Checked = Properties.ToolSettings.Default.setVehicle_toolOffset > 0;
            rbtnLeftNegative.Checked = Properties.ToolSettings.Default.setVehicle_toolOffset < 0;
        }

        private void btnZeroToolOffset_Click(object sender, EventArgs e)
        {
            nudOffset.Value = 0;
            rbtnToolRightPositive.Checked = false;
            rbtnLeftNegative.Checked = false;

            mf.tool.offset = 0;
            Properties.ToolSettings.Default.setVehicle_toolOffset = mf.tool.offset;
        }

        private void rbtnToolRightPositive_Click(object sender, EventArgs e)
        {
            if (rbtnToolRightPositive.Checked)
                mf.tool.offset = (double)nudOffset.Value * mf.inchOrCm2m;
            else
                mf.tool.offset = (double)nudOffset.Value * -mf.inchOrCm2m;
            Properties.ToolSettings.Default.setVehicle_toolOffset = mf.tool.offset;

            rbtnToolRightPositive.Checked = false;
            rbtnLeftNegative.Checked = false;
            rbtnToolRightPositive.Checked = Properties.ToolSettings.Default.setVehicle_toolOffset > 0;
            rbtnLeftNegative.Checked = Properties.ToolSettings.Default.setVehicle_toolOffset < 0;
        }

        private void nudOverlap_Click(object sender, EventArgs e)
        {
            if (((NudlessNumericUpDown)sender).ShowKeypad(this))
            {
                if (!rbtnToolOverlap.Checked && !rbtnToolGap.Checked)
                    rbtnToolOverlap.Checked = true;

                if (rbtnToolOverlap.Checked)
                    mf.tool.overlap = (double)nudOverlap.Value * mf.inchOrCm2m;
                else
                    mf.tool.overlap = (double)nudOverlap.Value * -mf.inchOrCm2m;

                Properties.ToolSettings.Default.setVehicle_toolOverlap = mf.tool.overlap;
            }

            rbtnToolOverlap.Checked = false;
            rbtnToolGap.Checked = false;
            rbtnToolOverlap.Checked = Properties.ToolSettings.Default.setVehicle_toolOverlap > 0;
            rbtnToolGap.Checked = Properties.ToolSettings.Default.setVehicle_toolOverlap < 0;
        }

        private void btnZeroOverlap_Click(object sender, EventArgs e)
        {
            nudOverlap.Value = 0;
            rbtnToolOverlap.Checked = false;
            rbtnToolGap.Checked = false;

            mf.tool.overlap = 0;
            Properties.ToolSettings.Default.setVehicle_toolOverlap = mf.tool.overlap;
        }

        private void rbtnToolOverlap_Click(object sender, EventArgs e)
        {
            if (rbtnToolOverlap.Checked)
                mf.tool.overlap = (double)nudOverlap.Value * mf.inchOrCm2m;
            else
                mf.tool.overlap = (double)nudOverlap.Value * -mf.inchOrCm2m;
            Properties.ToolSettings.Default.setVehicle_toolOverlap = mf.tool.overlap;
            Properties.ToolSettings.Default.Save();

            rbtnToolOverlap.Checked = false;
            rbtnToolGap.Checked = false;
            rbtnToolOverlap.Checked = Properties.ToolSettings.Default.setVehicle_toolOverlap > 0;
            rbtnToolGap.Checked = Properties.ToolSettings.Default.setVehicle_toolOverlap < 0;
        }

        #endregion

        #region PivotDistance

        private void tabToolPivot_Enter(object sender, EventArgs e)
        {
            nudTrailingToolToPivotLength.Value = (decimal)(Math.Abs(Properties.ToolSettings.Default.setTool_trailingToolToPivotLength) * mf.m2InchOrCm);

            rbtnPivotBehindPos.Checked = false;
            rbtnPivotAheadNeg.Checked = false;
            rbtnPivotBehindPos.Checked = Properties.ToolSettings.Default.setTool_trailingToolToPivotLength > 0;
            rbtnPivotAheadNeg.Checked = Properties.ToolSettings.Default.setTool_trailingToolToPivotLength < 0;

            LabelCm.Text = mf.unitsInCm;
        }

        private void tabToolPivot_Leave(object sender, EventArgs e)
        {
            Properties.ToolSettings.Default.Save();
        }


        private void btnPivotOffsetZero_Click(object sender, EventArgs e)
        {
            nudTrailingToolToPivotLength.Value = 0;
            rbtnPivotBehindPos.Checked = false;
            rbtnPivotAheadNeg.Checked = false;

            mf.tool.trailingToolToPivotLength = 0;
            Properties.ToolSettings.Default.setTool_trailingToolToPivotLength = mf.tool.trailingToolToPivotLength;
        }


        private void rbtnPivotBehindPos_Click(object sender, EventArgs e)
        {
            if (rbtnPivotBehindPos.Checked)
                mf.tool.trailingToolToPivotLength = (double)nudTrailingToolToPivotLength.Value * mf.inchOrCm2m;
            else
                mf.tool.trailingToolToPivotLength = (double)nudTrailingToolToPivotLength.Value * -mf.inchOrCm2m;
            Properties.ToolSettings.Default.setTool_trailingToolToPivotLength = mf.tool.trailingToolToPivotLength;

            rbtnPivotBehindPos.Checked = false;
            rbtnPivotAheadNeg.Checked = false;
            rbtnPivotBehindPos.Checked = Properties.ToolSettings.Default.setTool_trailingToolToPivotLength > 0;
            rbtnPivotAheadNeg.Checked = Properties.ToolSettings.Default.setTool_trailingToolToPivotLength < 0;
        }

        private void nudTrailingToolToPivotLength_Click(object sender, EventArgs e)
        {
            if (((NudlessNumericUpDown)sender).ShowKeypad(this))
            {
                if (rbtnPivotBehindPos.Checked)
                    mf.tool.trailingToolToPivotLength = (double)nudTrailingToolToPivotLength.Value * mf.inchOrCm2m;
                else
                    mf.tool.trailingToolToPivotLength = (double)nudTrailingToolToPivotLength.Value * -mf.inchOrCm2m;

                Properties.ToolSettings.Default.setTool_trailingToolToPivotLength = mf.tool.trailingToolToPivotLength;
            }

            rbtnPivotBehindPos.Checked = false;
            rbtnPivotAheadNeg.Checked = false;
            rbtnPivotBehindPos.Checked = Properties.ToolSettings.Default.setTool_trailingToolToPivotLength > 0;
            rbtnPivotAheadNeg.Checked = Properties.ToolSettings.Default.setTool_trailingToolToPivotLength < 0;
        }


        #endregion

        #region Sections 

        private void tabTSections_Enter(object sender, EventArgs e)
        {
            if (mf.isJobStarted)
            {
                if (mf.autoBtnState == btnStates.Auto)
                    mf.btnSectionMasterAuto.PerformClick();

                if (mf.manualBtnState == btnStates.On)
                    mf.btnSectionMasterManual.PerformClick();
            }

            if (mf.tool.isSectionsNotZones)
            {
                //fix ManualOffOnAuto buttons
                mf.manualBtnState = btnStates.Off;
                mf.btnSectionMasterManual.Image = Properties.Resources.ManualOff;

                //fix auto button
                mf.autoBtnState = btnStates.Off;
                mf.btnSectionMasterAuto.Image = Properties.Resources.SectionMasterOff;

                //Update the button colors and text
                mf.AllSectionsAndButtonsToState(mf.autoBtnState);

                //enable disable manual buttons
                mf.LineUpIndividualSectionBtns();

                nudDefaultSectionWidth.DecimalPlaces = 0;
            }
            else
            {
                //turn section buttons all OFF
                mf.AllZonesAndButtonsToState(btnStates.Off);

                mf.LineUpAllZoneButtons();

                nudDefaultSectionWidth.DecimalPlaces = 1;
            }

            cboxIsUnique.Checked = !mf.tool.isSectionsNotZones;

            cboxSectionBoundaryControl.Checked = Properties.ToolSettings.Default.setTool_isSectionOffWhenOut;
            if (cboxSectionBoundaryControl.Checked)
            {
                cboxSectionBoundaryControl.BackgroundImage = Properties.Resources.SectionOffBoundary;
            }
            else
            {
                cboxSectionBoundaryControl.BackgroundImage = Properties.Resources.SectionOnBoundary;
            }

            if (mf.isMetric)
            {
                nudCutoffSpeed.Minimum = 0;
                nudCutoffSpeed.Maximum = 30;
                nudCutoffSpeed.Value = (decimal)Properties.ToolSettings.Default.setVehicle_slowSpeedCutoff;
                lblTurnOffBelowUnits.Text = "Km/H";
            }
            else
            {
                nudCutoffSpeed.Minimum = 0;
                nudCutoffSpeed.Maximum = (decimal)Speed.KmhToMph(30);
                nudCutoffSpeed.Value = (decimal)Speed.KmhToMph(Properties.ToolSettings.Default.setVehicle_slowSpeedCutoff);
                lblTurnOffBelowUnits.Text = "MPH";
            }

            if (cboxIsUnique.Checked)
            {
                cboxIsUnique.BackgroundImage = Properties.Resources.ConT_Symmetric;
                cboxNumberOfZones.Visible = labelZonesBox.Visible = true;
            }
            else
            {
                cboxIsUnique.BackgroundImage = Properties.Resources.ConT_Asymmetric;
                cboxNumberOfZones.Visible = labelZonesBox.Visible = false;
            }

            nudNumberOfSections.Maximum = FormGPS.MAXSECTIONS;

            //fix ManualOffOnAuto buttons
            mf.manualBtnState = btnStates.Off;
            mf.btnSectionMasterManual.Image = Properties.Resources.ManualOff;

            //fix auto button
            mf.autoBtnState = btnStates.Off;
            mf.btnSectionMasterAuto.Image = Properties.Resources.SectionMasterOff;

            nudMinCoverage.Value = (decimal)Properties.ToolSettings.Default.setVehicle_minCoverage;

            if (mf.tool.isSectionsNotZones)
            {
                //Update the button colors and text
                mf.AllSectionsAndButtonsToState(btnStates.Off);

                //enable disable manual buttons
                mf.LineUpIndividualSectionBtns();

                numberOfSections = Properties.ToolSettings.Default.setVehicle_numSections;

                cboxNumSections.Text = numberOfSections.ToString();
                defaultSectionWidth = Properties.ToolSettings.Default.setTool_defaultSectionWidth;
                nudDefaultSectionWidth.Value = (int)(defaultSectionWidth * mf.m2InchOrCm);

                panelSymmetricSections.Visible = false;

                nudNumberOfSections.Visible = false;
                cboxNumSections.Visible = true;

                nudSection01.Value = Math.Abs((Properties.ToolSettings.Default.setSection_position2 - Properties.ToolSettings.Default.setSection_position1) * (decimal)mf.m2InchOrCm);
                nudSection02.Value = Math.Abs((Properties.ToolSettings.Default.setSection_position3 - Properties.ToolSettings.Default.setSection_position2) * (decimal)mf.m2InchOrCm);
                nudSection03.Value = Math.Abs((Properties.ToolSettings.Default.setSection_position4 - Properties.ToolSettings.Default.setSection_position3) * (decimal)mf.m2InchOrCm);
                nudSection04.Value = Math.Abs((Properties.ToolSettings.Default.setSection_position5 - Properties.ToolSettings.Default.setSection_position4) * (decimal)mf.m2InchOrCm);
                nudSection05.Value = Math.Abs((Properties.ToolSettings.Default.setSection_position6 - Properties.ToolSettings.Default.setSection_position5) * (decimal)mf.m2InchOrCm);
                nudSection06.Value = Math.Abs((Properties.ToolSettings.Default.setSection_position7 - Properties.ToolSettings.Default.setSection_position6) * (decimal)mf.m2InchOrCm);
                nudSection07.Value = Math.Abs((Properties.ToolSettings.Default.setSection_position8 - Properties.ToolSettings.Default.setSection_position7) * (decimal)mf.m2InchOrCm);
                nudSection08.Value = Math.Abs((Properties.ToolSettings.Default.setSection_position9 - Properties.ToolSettings.Default.setSection_position8) * (decimal)mf.m2InchOrCm);
                nudSection09.Value = Math.Abs((Properties.ToolSettings.Default.setSection_position10 - Properties.ToolSettings.Default.setSection_position9) * (decimal)mf.m2InchOrCm);
                nudSection10.Value = Math.Abs((Properties.ToolSettings.Default.setSection_position11 - Properties.ToolSettings.Default.setSection_position10) * (decimal)mf.m2InchOrCm);
                nudSection11.Value = Math.Abs((Properties.ToolSettings.Default.setSection_position12 - Properties.ToolSettings.Default.setSection_position11) * (decimal)mf.m2InchOrCm);
                nudSection12.Value = Math.Abs((Properties.ToolSettings.Default.setSection_position13 - Properties.ToolSettings.Default.setSection_position12) * (decimal)mf.m2InchOrCm);
                nudSection13.Value = Math.Abs((Properties.ToolSettings.Default.setSection_position14 - Properties.ToolSettings.Default.setSection_position13) * (decimal)mf.m2InchOrCm);
                nudSection14.Value = Math.Abs((Properties.ToolSettings.Default.setSection_position15 - Properties.ToolSettings.Default.setSection_position14) * (decimal)mf.m2InchOrCm);
                nudSection15.Value = Math.Abs((Properties.ToolSettings.Default.setSection_position16 - Properties.ToolSettings.Default.setSection_position15) * (decimal)mf.m2InchOrCm);
                nudSection16.Value = Math.Abs((Properties.ToolSettings.Default.setSection_position17 - Properties.ToolSettings.Default.setSection_position16) * (decimal)mf.m2InchOrCm);

                //based on number of sections and values update the page before displaying
                UpdateSpinners();
            }
            else
            {
                //turn section buttons all OFF
                mf.AllZonesAndButtonsToState(btnStates.Off);

                cboxNumSections.Visible = false;

                panelSymmetricSections.Visible = true;
                nudNumberOfSections.Visible = true;

                numberOfSections = Properties.ToolSettings.Default.setTool_numSectionsMulti;
                nudNumberOfSections.Value = numberOfSections;

                defaultSectionWidth = Properties.ToolSettings.Default.setTool_sectionWidthMulti;
                nudDefaultSectionWidth.Value = (decimal)(Math.Round((defaultSectionWidth * mf.m2InchOrCm), 1));

                SetNudZoneMinMax();

                nudZone1To.Value = mf.tool.zoneRanges[1];
                nudZone2To.Value = mf.tool.zoneRanges[2];
                nudZone3To.Value = mf.tool.zoneRanges[3];
                nudZone4To.Value = mf.tool.zoneRanges[4];
                nudZone5To.Value = mf.tool.zoneRanges[5];
                nudZone6To.Value = mf.tool.zoneRanges[6];
                nudZone7To.Value = mf.tool.zoneRanges[7];
                nudZone8To.Value = mf.tool.zoneRanges[8];

                cboxNumberOfZones.SelectedIndexChanged -= cboxNumberOfZones_SelectedIndexChanged;
                cboxNumberOfZones.Text = mf.tool.zones.ToString();
                cboxNumberOfZones.SelectedIndexChanged += cboxNumberOfZones_SelectedIndexChanged;

                words = Properties.ToolSettings.Default.setTool_zones.Split(',');
                lblVehicleToolWidth.Text = Convert.ToString((int)(numberOfSections * defaultSectionWidth * 100 * mf.cm2CmOrIn));

                mf.LineUpAllZoneButtons();
                SetNudZoneVisibility();
            }

            label178.Text = mf.unitsInCm;
        }

        private void tabTSections_Leave(object sender, EventArgs e)
        {
            if (mf.tool.isSectionsNotZones)
            {
                //take the section widths and convert to meters and positions along tool.
                CalculateSectionPositions();

                Properties.ToolSettings.Default.setTool_isSectionOffWhenOut = cboxSectionBoundaryControl.Checked;
                mf.tool.isSectionOffWhenOut = cboxSectionBoundaryControl.Checked;

                //save the values in each spinner for section position widths in settings
                Properties.ToolSettings.Default.setSection_position1 = sectionPositionArr[0];
                Properties.ToolSettings.Default.setSection_position2 = sectionPositionArr[1];
                Properties.ToolSettings.Default.setSection_position3 = sectionPositionArr[2];
                Properties.ToolSettings.Default.setSection_position4 = sectionPositionArr[3];
                Properties.ToolSettings.Default.setSection_position5 = sectionPositionArr[4];
                Properties.ToolSettings.Default.setSection_position6 = sectionPositionArr[5];
                Properties.ToolSettings.Default.setSection_position7 = sectionPositionArr[6];
                Properties.ToolSettings.Default.setSection_position8 = sectionPositionArr[7];
                Properties.ToolSettings.Default.setSection_position9 = sectionPositionArr[8];
                Properties.ToolSettings.Default.setSection_position10 = sectionPositionArr[9];
                Properties.ToolSettings.Default.setSection_position11 = sectionPositionArr[10];
                Properties.ToolSettings.Default.setSection_position12 = sectionPositionArr[11];
                Properties.ToolSettings.Default.setSection_position13 = sectionPositionArr[12];
                Properties.ToolSettings.Default.setSection_position14 = sectionPositionArr[13];
                Properties.ToolSettings.Default.setSection_position15 = sectionPositionArr[14];
                Properties.ToolSettings.Default.setSection_position16 = sectionPositionArr[15];
                Properties.ToolSettings.Default.setSection_position17 = sectionPositionArr[16];

                mf.tool.numOfSections = numberOfSections;

                Properties.ToolSettings.Default.setVehicle_numSections = mf.tool.numOfSections;

                //line up manual buttons based on # of sections
                mf.LineUpIndividualSectionBtns();

                //update the sections to newly configured widths and positions in main
                mf.SectionSetPosition();

                //update the widths of sections and tool width in main
                mf.SectionCalcWidths();

                mf.tram.IsTramOuterOrInner();

                Properties.ToolSettings.Default.setVehicle_toolWidth = mf.tool.width;

                mf.SendRelaySettingsToMachineModule();
            }
            else
            {
                mf.tool.numOfSections = numberOfSections;
                Properties.ToolSettings.Default.setTool_numSectionsMulti = mf.tool.numOfSections;

                mf.tool.width = numberOfSections * defaultSectionWidth;
                Properties.ToolSettings.Default.setVehicle_toolWidth = mf.tool.width;

                mf.tram.IsTramOuterOrInner();

                Properties.ToolSettings.Default.Save();

                mf.SectionCalcMulti();

                for (int i = 0; i < 9; i++)
                {
                    mf.tool.zoneRanges[i] = 0;
                }

                mf.tool.zoneRanges[0] = mf.tool.zones;

                if (mf.tool.zones == 2)
                {
                    mf.tool.zoneRanges[1] = (int)nudZone1To.Value;
                    mf.tool.zoneRanges[2] = (int)nudZone2To.Value;
                }
                else if (mf.tool.zones == 3)
                {
                    mf.tool.zoneRanges[1] = (int)nudZone1To.Value;
                    mf.tool.zoneRanges[2] = (int)nudZone2To.Value;
                    mf.tool.zoneRanges[3] = (int)nudZone3To.Value;
                }
                else if (mf.tool.zones == 4)
                {
                    mf.tool.zoneRanges[1] = (int)nudZone1To.Value;
                    mf.tool.zoneRanges[2] = (int)nudZone2To.Value;
                    mf.tool.zoneRanges[3] = (int)nudZone3To.Value;
                    mf.tool.zoneRanges[4] = (int)nudZone4To.Value;
                }
                else if (mf.tool.zones == 5)
                {
                    mf.tool.zoneRanges[1] = (int)nudZone1To.Value;
                    mf.tool.zoneRanges[2] = (int)nudZone2To.Value;
                    mf.tool.zoneRanges[3] = (int)nudZone3To.Value;
                    mf.tool.zoneRanges[4] = (int)nudZone4To.Value;
                    mf.tool.zoneRanges[5] = (int)nudZone5To.Value;
                }
                else if (mf.tool.zones == 6)
                {
                    mf.tool.zoneRanges[1] = (int)nudZone1To.Value;
                    mf.tool.zoneRanges[2] = (int)nudZone2To.Value;
                    mf.tool.zoneRanges[3] = (int)nudZone3To.Value;
                    mf.tool.zoneRanges[4] = (int)nudZone4To.Value;
                    mf.tool.zoneRanges[5] = (int)nudZone5To.Value;
                    mf.tool.zoneRanges[6] = (int)nudZone6To.Value;
                }
                else if (mf.tool.zones == 7)
                {
                    mf.tool.zoneRanges[1] = (int)nudZone1To.Value;
                    mf.tool.zoneRanges[2] = (int)nudZone2To.Value;
                    mf.tool.zoneRanges[3] = (int)nudZone3To.Value;
                    mf.tool.zoneRanges[4] = (int)nudZone4To.Value;
                    mf.tool.zoneRanges[5] = (int)nudZone5To.Value;
                    mf.tool.zoneRanges[6] = (int)nudZone6To.Value;
                    mf.tool.zoneRanges[7] = (int)nudZone7To.Value;
                }
                else if (mf.tool.zones == 8)
                {
                    mf.tool.zoneRanges[1] = (int)nudZone1To.Value;
                    mf.tool.zoneRanges[2] = (int)nudZone2To.Value;
                    mf.tool.zoneRanges[3] = (int)nudZone3To.Value;
                    mf.tool.zoneRanges[4] = (int)nudZone4To.Value;
                    mf.tool.zoneRanges[5] = (int)nudZone5To.Value;
                    mf.tool.zoneRanges[6] = (int)nudZone6To.Value;
                    mf.tool.zoneRanges[7] = (int)nudZone7To.Value;
                    mf.tool.zoneRanges[8] = (int)nudZone8To.Value;
                }

                String str = "";
                str = String.Join(",", mf.tool.zoneRanges);
                Properties.ToolSettings.Default.setTool_zones = str;

                mf.LineUpAllZoneButtons();
            }

            //no multi color zones
            if (mf.tool.isSectionsNotZones)
                Properties.ToolSettings.Default.setColor_isMultiColorSections = mf.tool.isMultiColoredSections = false;

            Properties.ToolSettings.Default.Save();
        }

        private void nudZone1To_Click(object sender, EventArgs e)
        {
            if (((NudlessNumericUpDown)sender).ShowKeypad(this))
            {
                mf.tool.zoneRanges[1] = (int)nudZone1To.Value;
                SetNudZoneVisibility();
            }
        }

        private void nudZone2To_Click(object sender, EventArgs e)
        {
            if (((NudlessNumericUpDown)sender).ShowKeypad(this))
            {
                mf.tool.zoneRanges[2] = (int)nudZone2To.Value;
                SetNudZoneVisibility();
            }
        }

        private void nudZone3To_Click(object sender, EventArgs e)
        {
            if (((NudlessNumericUpDown)sender).ShowKeypad(this))
            {
                mf.tool.zoneRanges[3] = (int)nudZone3To.Value;
                SetNudZoneVisibility();
            }
        }

        private void nudZone4To_Click(object sender, EventArgs e)
        {
            if (((NudlessNumericUpDown)sender).ShowKeypad(this))
            {
                mf.tool.zoneRanges[4] = (int)nudZone4To.Value;
                SetNudZoneVisibility();
            }
        }

        private void nudZone5To_Click(object sender, EventArgs e)
        {
            if (((NudlessNumericUpDown)sender).ShowKeypad(this))
            {
                mf.tool.zoneRanges[5] = (int)nudZone5To.Value;
                SetNudZoneVisibility();
            }
        }

        private void nudZone6To_Click(object sender, EventArgs e)
        {
            if (((NudlessNumericUpDown)sender).ShowKeypad(this))
            {
                mf.tool.zoneRanges[6] = (int)nudZone6To.Value;
                SetNudZoneVisibility();
            }
        }

        private void nudZone7To_Click(object sender, EventArgs e)
        {
            if (((NudlessNumericUpDown)sender).ShowKeypad(this))
            {
                mf.tool.zoneRanges[7] = (int)nudZone7To.Value;
                SetNudZoneVisibility();
            }
        }

        private void nudZone8To_Click(object sender, EventArgs e)
        {
            if (((NudlessNumericUpDown)sender).ShowKeypad(this))
            {
                mf.tool.zoneRanges[8] = (int)nudZone8To.Value;
                SetNudZoneVisibility();
            }
        }

        private void cboxNumberOfZones_SelectedIndexChanged(object sender, EventArgs e)
        {
            if ((cboxNumberOfZones.SelectedIndex + 2) > (int)nudNumberOfSections.Value)
            {
                mf.YesMessageBox("You can't have more zones then sections");
                cboxNumberOfZones.SelectedIndex = mf.tool.zones - 2;

                return;
            }

            mf.tool.zones = cboxNumberOfZones.SelectedIndex + 2;
            //if (mf.tool.zones == 1) mf.tool.zones = 0;


            SetNudZoneMinMax();
            FillZoneNudsWithDefaultValues();
            SetNudZoneVisibility();
        }

        private void FillZoneNudsWithDefaultValues()
        {
            nudZone1To.Value = 0;
            nudZone2To.Value = 0;
            nudZone3To.Value = 0;
            nudZone4To.Value = 0;
            nudZone5To.Value = 0;
            nudZone6To.Value = 0;
            nudZone7To.Value = 0;
            nudZone8To.Value = 0;

            if (mf.tool.zones != 0)
            {
                int defa = numberOfSections / mf.tool.zones;
                if (mf.tool.zones == 2)
                {
                    nudZone1To.Value += defa;
                    nudZone2To.Value = numberOfSections;
                }
                else if (mf.tool.zones == 3)
                {
                    nudZone1To.Value += defa;
                    nudZone2To.Value += 2 * defa;
                    nudZone3To.Value = numberOfSections;
                }
                else if (mf.tool.zones == 4)
                {
                    nudZone1To.Value += defa;
                    nudZone2To.Value += 2 * defa;
                    nudZone3To.Value += 3 * defa;
                    nudZone4To.Value = numberOfSections;
                }
                else if (mf.tool.zones == 5)
                {
                    nudZone1To.Value += defa;
                    nudZone2To.Value += 2 * defa;
                    nudZone3To.Value += 3 * defa;
                    nudZone4To.Value += 4 * defa;
                    nudZone5To.Value = numberOfSections;
                }
                else if (mf.tool.zones == 6)
                {
                    nudZone1To.Value += defa;
                    nudZone2To.Value += 2 * defa;
                    nudZone3To.Value += 3 * defa;
                    nudZone4To.Value += 4 * defa;
                    nudZone5To.Value += 5 * defa;
                    nudZone6To.Value = numberOfSections;
                }
                else if (mf.tool.zones == 7)
                {
                    nudZone1To.Value += defa;
                    nudZone2To.Value += 2 * defa;
                    nudZone3To.Value += 3 * defa;
                    nudZone4To.Value += 4 * defa;
                    nudZone5To.Value += 5 * defa;
                    nudZone6To.Value += 6 * defa;
                    nudZone7To.Value = numberOfSections;
                }
                else if (mf.tool.zones == 8)
                {
                    nudZone1To.Value += defa;
                    nudZone2To.Value += 2 * defa;
                    nudZone3To.Value += 3 * defa;
                    nudZone4To.Value += 4 * defa;
                    nudZone5To.Value += 5 * defa;
                    nudZone6To.Value += 6 * defa;
                    nudZone7To.Value += 7 * defa;
                    nudZone8To.Value = numberOfSections;
                }
            }
        }

        private void SetNudZoneMinMax()
        {
            nudZone1To.Maximum = numberOfSections;
            nudZone2To.Maximum = numberOfSections;
            nudZone3To.Maximum = numberOfSections;
            nudZone4To.Maximum = numberOfSections;
            nudZone5To.Maximum = numberOfSections;
            nudZone6To.Maximum = numberOfSections;
            nudZone7To.Maximum = numberOfSections;
            nudZone8To.Maximum = numberOfSections;
        }

        private void SetNudZoneVisibility()
        {
            nudZone1To.Visible = false;
            nudZone2To.Visible = false;
            nudZone3To.Visible = false;
            nudZone4To.Visible = false;
            nudZone5To.Visible = false;
            nudZone6To.Visible = false;
            nudZone7To.Visible = false;
            nudZone8To.Visible = false;

            nudZone1To.Enabled = true;
            nudZone2To.Enabled = true;
            nudZone3To.Enabled = true;
            nudZone4To.Enabled = true;
            nudZone5To.Enabled = true;
            nudZone6To.Enabled = true;
            nudZone7To.Enabled = true;
            nudZone8To.Enabled = true;

            lblZoneStart1.Visible = false;
            lblZoneStart2.Visible = false;
            lblZoneStart3.Visible = false;
            lblZoneStart4.Visible = false;
            lblZoneStart5.Visible = false;
            lblZoneStart6.Visible = false;
            lblZoneStart7.Visible = false;
            lblZoneStart8.Visible = false;

            if (mf.tool.zones > 1)
            {
                nudZone2To.Visible = true;
                nudZone1To.Visible = true;
                lblZoneStart1.Visible = true;
                lblZoneStart2.Visible = true;
                lblZoneStart2.Text = (nudZone1To.Value + 1).ToString();
                if (mf.tool.zones == 2) nudZone2To.Enabled = false;
            }

            if (mf.tool.zones > 2)
            {
                nudZone3To.Visible = true;
                lblZoneStart3.Visible = true;
                lblZoneStart3.Text = (nudZone2To.Value + 1).ToString();
                if (mf.tool.zones == 3) nudZone3To.Enabled = false;
            }

            if (mf.tool.zones > 3)
            {
                nudZone4To.Visible = true;
                lblZoneStart4.Visible = true;
                lblZoneStart4.Text = (nudZone3To.Value + 1).ToString();
                if (mf.tool.zones == 4) nudZone4To.Enabled = false;
            }

            if (mf.tool.zones > 4)
            {
                nudZone5To.Visible = true;
                lblZoneStart5.Visible = true;
                lblZoneStart5.Text = (nudZone4To.Value + 1).ToString();
                if (mf.tool.zones == 5) nudZone5To.Enabled = false;
            }

            if (mf.tool.zones > 5)
            {
                nudZone6To.Visible = true;
                lblZoneStart6.Visible = true;
                lblZoneStart6.Text = (nudZone5To.Value + 1).ToString();
                if (mf.tool.zones == 6) nudZone6To.Enabled = false;
            }

            if (mf.tool.zones > 6)
            {
                nudZone7To.Visible = true;
                lblZoneStart7.Visible = true;
                lblZoneStart7.Text = (nudZone6To.Value + 1).ToString();
                if (mf.tool.zones == 7) nudZone7To.Enabled = false;
            }

            if (mf.tool.zones > 7)
            {
                nudZone8To.Visible = true;
                lblZoneStart8.Visible = true;
                lblZoneStart8.Text = (nudZone7To.Value + 1).ToString();
                if (mf.tool.zones == 8) nudZone8To.Enabled = false;
            }

        }

        private void cboxSectionBoundaryControl_Click(object sender, EventArgs e)
        {
            Properties.ToolSettings.Default.setTool_isSectionOffWhenOut = !Properties.ToolSettings.Default.setTool_isSectionOffWhenOut;
            Properties.ToolSettings.Default.Save();

            cboxSectionBoundaryControl.Checked = Properties.ToolSettings.Default.setTool_isSectionOffWhenOut;
            if (cboxSectionBoundaryControl.Checked)
            {
                cboxSectionBoundaryControl.BackgroundImage = Properties.Resources.SectionOffBoundary;
            }
            else
            {
                cboxSectionBoundaryControl.BackgroundImage = Properties.Resources.SectionOnBoundary;
            }
        }

        private void cboxIsUnique_Click(object sender, EventArgs e)
        {
            mf.tool.isSectionsNotZones = !cboxIsUnique.Checked;
            Properties.ToolSettings.Default.setTool_isSectionsNotZones = !cboxIsUnique.Checked;
            tabTSections_Enter(this, e);
        }

        private void nudNumberOfSections_Click(object sender, EventArgs e)
        {
            if (((NudlessNumericUpDown)sender).ShowKeypad(this))
            {
                if ((int)nudNumberOfSections.Value < mf.tool.zones)
                {
                    mf.YesMessageBox("You can't have more zones then sections");
                    nudNumberOfSections.Value = numberOfSections;
                    return;
                }
                numberOfSections = (int)nudNumberOfSections.Value;
                SetNudZoneMinMax();

                Properties.ToolSettings.Default.setTool_numSectionsMulti = numberOfSections;
                Properties.ToolSettings.Default.Save();

                lblVehicleToolWidth.Text = Convert.ToString((int)(numberOfSections * defaultSectionWidth * 100 * mf.cm2CmOrIn));
                SectionFeetInchesTotalWidthLabelUpdate(mf.isMetric, mf.tool.width);
                FillZoneNudsWithDefaultValues();
                SetNudZoneVisibility();
            }
        }

        private void nudDefaultSectionWidth_Click(object sender, EventArgs e)
        {
            if (((NudlessNumericUpDown)sender).ShowKeypad(this))
            {
                defaultSectionWidth = (double)nudDefaultSectionWidth.Value * mf.inchOrCm2m;

                if (mf.tool.isSectionsNotZones)
                    Properties.ToolSettings.Default.setTool_defaultSectionWidth = defaultSectionWidth;
                else
                    Properties.ToolSettings.Default.setTool_sectionWidthMulti = defaultSectionWidth;

                Properties.ToolSettings.Default.Save();

                //lblVehicleToolWidth.Text = Convert.ToString((int)(numberOfSections * defaultSectionWidth * 100 * mf.cm2CmOrIn));
                //SectionFeetInchesTotalWidthLabelUpdate();
            }
        }

        private void cboxNumSections_SelectedIndexChanged(object sender, EventArgs e)
        {
            if (mf.tool.isSectionsNotZones)
            {
                numberOfSections = cboxNumSections.SelectedIndex + 1;

                decimal wide = nudDefaultSectionWidth.Value;

                if (mf.isMetric)
                {
                    if (numberOfSections * wide > 5000)
                    {
                        wide = 99;
                        FormDialog.Show("Too Wide", "Max 50 Meters", DialogSeverity.Error);
                        Log.EventWriter("Sections, Tool Set Too Wide");

                    }
                }
                else
                {
                    if (numberOfSections * wide > 1900)
                    {
                        wide = 19;
                        FormDialog.Show("Too Wide", "Max 164 Feet", DialogSeverity.Error);
                        Log.EventWriter("Sections, Tool Set Too Wide");
                    }
                }

                nudSection01.Value = wide;
                nudSection02.Value = wide;
                nudSection03.Value = wide;
                nudSection04.Value = wide;
                nudSection05.Value = wide;
                nudSection06.Value = wide;
                nudSection07.Value = wide;
                nudSection08.Value = wide;
                nudSection09.Value = wide;
                nudSection10.Value = wide;
                nudSection11.Value = wide;
                nudSection12.Value = wide;
                nudSection13.Value = wide;
                nudSection14.Value = wide;
                nudSection15.Value = wide;
                nudSection16.Value = wide;

                UpdateSpinners();

                //take the section widths and convert to meters and positions along tool.
                CalculateSectionPositions();
                //line up manual buttons based on # of sections
                mf.LineUpIndividualSectionBtns();

                //update the sections to newly configured widths and positions in main
                mf.SectionSetPosition();

                //update the widths of sections and tool width in main
                mf.SectionCalcWidths();
            }
        }

        private void NudSection1_Click(object sender, EventArgs e)
        {
            ((NudlessNumericUpDown)sender).ShowKeypad(this);
            UpdateSpinners();
        }

        private void nudCutoffSpeed_Click(object sender, EventArgs e)
        {
            if (((NudlessNumericUpDown)sender).ShowKeypad(this))
            {
                // Convert from MPH to km/h if imperial units are selected
                double speedKmh = mf.isMetric ? (double)nudCutoffSpeed.Value : Speed.MphToKmh((double)nudCutoffSpeed.Value);
                mf.vehicle.slowSpeedCutoff = speedKmh;
                Properties.ToolSettings.Default.setVehicle_slowSpeedCutoff = speedKmh;
            }
        }

        private void nudMinCoverage_Click(object sender, EventArgs e)
        {
            if (((NudlessNumericUpDown)sender).ShowKeypad(this))
            {
                mf.tool.minCoverage = (int)nudMinCoverage.Value;
                Properties.ToolSettings.Default.setVehicle_minCoverage = mf.tool.minCoverage;
            }
        }

        public void UpdateSpinners()
        {
            int i = (int)numberOfSections;

            decimal toolWidth = 0;

            foreach (var item in tab1.TabPages[9].Controls)
            {
                if (item is NudlessNumericUpDown)
                {
                    var item2 = (NudlessNumericUpDown)item;
                    if (item2.Name.Substring(0, 6) == "nudSec")
                    {
                        //grab the number from nudSection01
                        string bob = item2.Name.Substring(10, 2);
                        int nudNum = Convert.ToInt32(bob);
                        if (nudNum <= i)
                        {
                            item2.Enabled = true;
                            item2.Visible = true;
                            toolWidth += item2.Value;

                            if (mf.isMetric)
                            {
                                if (toolWidth > 5000)
                                {
                                    FormDialog.Show("Too Wide", "Set to 99, Max 50 Meters", DialogSeverity.Error);
                                    Log.EventWriter("Sections, Tool Set Too Wide");
                                    toolWidth = 0;
                                    nudSection01.Value = 99;
                                    nudSection02.Value = 99;
                                    nudSection03.Value = 99;
                                    nudSection04.Value = 99;
                                    nudSection05.Value = 99;
                                    nudSection06.Value = 99;
                                    nudSection07.Value = 99;
                                    nudSection08.Value = 99;
                                    nudSection09.Value = 99;
                                    nudSection10.Value = 99;
                                    nudSection11.Value = 99;
                                    nudSection12.Value = 99;
                                    nudSection13.Value = 99;
                                    nudSection14.Value = 99;
                                    nudSection15.Value = 99;
                                    nudSection16.Value = 99;
                                }
                            }
                            else
                            {
                                if (toolWidth > 1900)
                                {
                                    FormDialog.Show("Too Wide", "Set to 99, Max 164 Feet", DialogSeverity.Error);
                                    Log.EventWriter("Sections, Tool Set Too Wide");
                                    toolWidth = 0;
                                    nudSection01.Value = 99;
                                    nudSection02.Value = 99;
                                    nudSection03.Value = 99;
                                    nudSection04.Value = 99;
                                    nudSection05.Value = 99;
                                    nudSection06.Value = 99;
                                    nudSection07.Value = 99;
                                    nudSection08.Value = 99;
                                    nudSection09.Value = 99;
                                    nudSection10.Value = 99;
                                    nudSection11.Value = 99;
                                    nudSection12.Value = 99;
                                    nudSection13.Value = 99;
                                    nudSection14.Value = 99;
                                    nudSection15.Value = 99;
                                    nudSection16.Value = 99;
                                }
                            }

                        }
                        else
                        {
                            item2.Enabled = false;
                            item2.Visible = false;
                        }
                    }
                }
            }

            lblVehicleToolWidth.Text = Convert.ToString((int)toolWidth);

            SectionFeetInchesTotalWidthLabelUpdate(mf.isMetric, mf.tool.width);
        }

        //update tool width label at bottom of window
        private void SectionFeetInchesTotalWidthLabelUpdate(bool isMetric, double toolWidthInMeters)
        {
            lblInchesCm.Text = isMetric ? gStr.gsCentimeters : gStr.gsInches;
            lblFeetMeters.Text = isMetric ? gStr.gsMeters : "Feet";

            lblSecTotalWidth.Text = Distance.MediumDistanceString(isMetric, toolWidthInMeters);
            configSummaryControl.SetSummaryWidth(lblSecTotalWidth.Text);
        }

        //Convert section width to positions along toolbar
        private void CalculateSectionPositions()
        {
            int i = numberOfSections;

            //convert to meters spinner value
            sectionWidthArr[0] = nudSection01.Value * (decimal)mf.inchOrCm2m;
            sectionWidthArr[1] = nudSection02.Value * (decimal)mf.inchOrCm2m;
            sectionWidthArr[2] = nudSection03.Value * (decimal)mf.inchOrCm2m;
            sectionWidthArr[3] = nudSection04.Value * (decimal)mf.inchOrCm2m;
            sectionWidthArr[4] = nudSection05.Value * (decimal)mf.inchOrCm2m;
            sectionWidthArr[5] = nudSection06.Value * (decimal)mf.inchOrCm2m;
            sectionWidthArr[6] = nudSection07.Value * (decimal)mf.inchOrCm2m;
            sectionWidthArr[7] = nudSection08.Value * (decimal)mf.inchOrCm2m;
            sectionWidthArr[8] = nudSection09.Value * (decimal)mf.inchOrCm2m;
            sectionWidthArr[9] = nudSection10.Value * (decimal)mf.inchOrCm2m;
            sectionWidthArr[10] = nudSection11.Value * (decimal)mf.inchOrCm2m;
            sectionWidthArr[11] = nudSection12.Value * (decimal)mf.inchOrCm2m;
            sectionWidthArr[12] = nudSection13.Value * (decimal)mf.inchOrCm2m;
            sectionWidthArr[13] = nudSection14.Value * (decimal)mf.inchOrCm2m;
            sectionWidthArr[14] = nudSection15.Value * (decimal)mf.inchOrCm2m;
            sectionWidthArr[15] = nudSection16.Value * (decimal)mf.inchOrCm2m;

            //add up the set widths
            decimal setWidth = 0;
            for (int j = 0; j < i; j++)
            {
                setWidth += sectionWidthArr[j];
            }

            //leftmost position.
            setWidth *= -0.5M;

            sectionPositionArr[0] = setWidth;

            for (int j = 1; j < 17; j++)
            {
                if (j <= i) sectionPositionArr[j] = sectionPositionArr[j - 1] + sectionWidthArr[j - 1];
                else sectionPositionArr[j] = 0;
            }
        }

        #endregion

        #region Switch

        private void tabTSwitches_Enter(object sender, EventArgs e)
        {
            //set accordingly
            chkSelectSteerSwitch.Checked = mf.mc.isSteerWorkSwitchEnabled;
            chkSelectWorkSwitch.Checked = mf.mc.isWorkSwitchEnabled;

            if (Properties.Settings.Default.setF_isWorkSwitchManualSections)
            {
                chkSetManualSections.Checked = true;
                chkSetAutoSections.Checked = false;
            }
            else
            {
                chkSetManualSections.Checked = false;
                chkSetAutoSections.Checked = true;
            }

            if (Properties.Settings.Default.setF_isSteerWorkSwitchManualSections)
            {
                chkSetManualSectionsSteer.Checked = true;
                chkSetAutoSectionsSteer.Checked = false;
            }
            else
            {
                chkSetManualSectionsSteer.Checked = false;
                chkSetAutoSectionsSteer.Checked = true;
            }

            chkSetManualSections.Enabled = chkSetAutoSections.Enabled = chkWorkSwActiveLow.Enabled = chkSelectWorkSwitch.Checked;
            chkSetManualSectionsSteer.Enabled = chkSetAutoSectionsSteer.Enabled = chkSelectSteerSwitch.Checked;


            chkWorkSwActiveLow.Checked = Properties.Settings.Default.setF_isWorkSwitchActiveLow;
            if (chkWorkSwActiveLow.Checked) chkWorkSwActiveLow.Image = Properties.Resources.SwitchActiveClosed;
            else chkWorkSwActiveLow.Image = Properties.Resources.SwitchActiveOpen;
        }

        private void tabTSwitches_Leave(object sender, EventArgs e)
        {
            //active low on work switch
            mf.mc.isWorkSwitchActiveLow = Properties.Settings.Default.setF_isWorkSwitchActiveLow = chkWorkSwActiveLow.Checked;

            //is work switch enabled
            mf.mc.isWorkSwitchEnabled = Properties.Settings.Default.setF_isWorkSwitchEnabled = chkSelectWorkSwitch.Checked;

            //Are auto or manual sections controlled. 
            mf.mc.isWorkSwitchManualSections = Properties.Settings.Default.setF_isWorkSwitchManualSections = chkSetManualSections.Checked;

            //Are auto or manual sections controlled for steer
            mf.mc.isSteerWorkSwitchEnabled = Properties.ToolSettings.Default.setF_isSteerWorkSwitchEnabled = chkSelectSteerSwitch.Checked;

            //does steer switch control manual or auto sections
            mf.mc.isSteerWorkSwitchManualSections = Properties.Settings.Default.setF_isSteerWorkSwitchManualSections = chkSetManualSectionsSteer.Checked;

            if (!mf.mc.isSteerWorkSwitchEnabled && !mf.mc.isWorkSwitchEnabled)
                mf.mc.isRemoteWorkSystemOn = Properties.Settings.Default.setF_isRemoteWorkSystemOn = false;
            else
                mf.mc.isRemoteWorkSystemOn = Properties.Settings.Default.setF_isRemoteWorkSystemOn = true;
            //save
            Properties.Settings.Default.Save();
        }

        private void chkSelectWorkSwitch_Click(object sender, EventArgs e)
        {
            chkSetManualSections.Enabled = chkSetAutoSections.Enabled = chkWorkSwActiveLow.Enabled = chkSelectWorkSwitch.Checked;
        }

        private void chkSelectSteerSwitch_Click(object sender, EventArgs e)
        {
            chkSetManualSectionsSteer.Enabled = chkSetAutoSectionsSteer.Enabled = chkSelectSteerSwitch.Checked;
        }

        private void chkWorkSwActiveLow_Click(object sender, EventArgs e)
        {
            if (chkWorkSwActiveLow.Checked) chkWorkSwActiveLow.Image = Properties.Resources.SwitchActiveClosed;
            else chkWorkSwActiveLow.Image = Properties.Resources.SwitchActiveOpen;
        }

        private void chkSetManualSections_Click(object sender, EventArgs e)
        {
            chkSetAutoSections.Checked = false;
            chkSetManualSections.Checked = true;
        }

        private void chkSetAutoSections_Click(object sender, EventArgs e)
        {
            chkSetManualSections.Checked = false;
            chkSetAutoSections.Checked = true;
        }

        private void chkSetAutoSectionsSteer_Click(object sender, EventArgs e)
        {
            chkSetManualSectionsSteer.Checked = false;
            chkSetAutoSectionsSteer.Checked = true;
        }

        private void chkSetManualSectionsSteer_Click(object sender, EventArgs e)
        {
            chkSetAutoSectionsSteer.Checked = false;
            chkSetManualSectionsSteer.Checked = true;
        }
        #endregion
    }
}
