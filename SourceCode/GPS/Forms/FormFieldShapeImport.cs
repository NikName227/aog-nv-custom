using AgLibrary.Logging;
using AgOpenGPS.Core.Models;
using AgOpenGPS.Forms;
using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;
using System.Windows.Forms;

namespace AgOpenGPS
{
    /// <summary>
    /// Import fields from a shapefile (ZIP/SHP): shows a table of found fields with a
    /// per-field import checkbox, an editable AOG name, and all shapefile attributes.
    /// Checked rows are created as AOG fields via the existing KML field pipeline.
    /// </summary>
    public partial class FormFieldShapeImport : Form
    {
        private readonly FormGPS mf;

        public FormFieldShapeImport(Form callingForm)
        {
            mf = callingForm as FormGPS;
            InitializeComponent();
        }

        private void btnBrowse_Click(object sender, EventArgs e)
        {
            using (var ofd = new OpenFileDialog
            {
                Filter = "Shapefile (*.zip;*.shp)|*.zip;*.shp|Zip (*.zip)|*.zip|Shapefile (*.shp)|*.shp",
                Title = "Select shapefile ZIP or SHP",
                InitialDirectory = RegistrySettings.fieldsDirectory
            })
            {
                if (ofd.ShowDialog(this) != DialogResult.OK) return;

                lblFile.Text = ofd.FileName;
                var feats = ShapeImporter.ReadFeatures(ofd.FileName, out string error);

                if (error != null)
                {
                    FormDialog.Show("Error", error, DialogSeverity.Error);
                    lblStatus.Text = "Read error.";
                    return;
                }
                if (feats.Count == 0)
                {
                    dgvFields.Columns.Clear();
                    lblStatus.Text = "No polygon fields found.";
                    return;
                }
                PopulateGrid(feats);
            }
        }

        private void PopulateGrid(List<ShapeFeatureInfo> feats)
        {
            dgvFields.Columns.Clear();
            dgvFields.Rows.Clear();

            dgvFields.Columns.Add(new DataGridViewCheckBoxColumn
            { Name = "chkImport", HeaderText = "Import", Width = 60 });
            dgvFields.Columns.Add(new DataGridViewTextBoxColumn
            { Name = "aogName", HeaderText = "AOG Name", Width = 240 });

            foreach (string attr in ShapeImporter.AttributeNames)
            {
                dgvFields.Columns.Add(new DataGridViewTextBoxColumn
                { Name = "attr_" + attr, HeaderText = attr, ReadOnly = true, Width = 110 });
            }

            foreach (var feat in feats)
            {
                int idx = dgvFields.Rows.Add();
                var row = dgvFields.Rows[idx];
                row.Cells["chkImport"].Value = true;
                row.Cells["aogName"].Value = feat.SuggestedName;

                foreach (var kv in feat.Attributes)
                {
                    string col = "attr_" + kv.Key;
                    if (dgvFields.Columns.Contains(col))
                        row.Cells[col].Value = kv.Value;
                }
                row.Tag = feat;
            }

            lblStatus.Text = feats.Count + " field(s) found.";
        }

        private async void btnImport_Click(object sender, EventArgs e)
        {
            int count = 0;
            try
            {
                using (var kmlForm = new FormFieldKML(mf))
                {
                    foreach (DataGridViewRow row in dgvFields.Rows)
                    {
                        if (!(row.Tag is ShapeFeatureInfo feat) || !IsChecked(row)) continue;

                        string name = row.Cells["aogName"].Value?.ToString()?.Trim();
                        if (string.IsNullOrEmpty(name)) name = feat.SuggestedName;
                        name = MakeUniqueName(name);

                        if (mf.isJobStarted)
                            await mf.FileSaveEverythingBeforeClosingField();

                        string kml = WriteTempKml(name, feat.Boundary);
                        kmlForm.CreateFieldFromKml(kml, name);
                        try { File.Delete(kml); } catch { }

                        count++;
                    }
                }

                if (mf.isJobStarted)
                    await mf.FileSaveEverythingBeforeClosingField();

                FormDialog.Show("Shape File Import", count + " field(s) imported.", DialogSeverity.Info);
                DialogResult = DialogResult.OK;
                Close();
            }
            catch (Exception ex)
            {
                Log.EventWriter("Shape import error " + ex.ToString());
                FormDialog.Show("Error", ex.Message, DialogSeverity.Error);
            }
        }

        private void btnCancel_Click(object sender, EventArgs e)
        {
            DialogResult = DialogResult.Cancel;
            Close();
        }

        private bool IsChecked(DataGridViewRow row)
        {
            return row.Cells["chkImport"].Value is bool b && b;
        }

        private string MakeUniqueName(string name)
        {
            string dir = RegistrySettings.fieldsDirectory;
            string candidate = name;
            int n = 2;
            while (Directory.Exists(Path.Combine(dir, candidate)))
            {
                candidate = name + "_" + n;
                n++;
            }
            return candidate;
        }

        private string WriteTempKml(string name, List<Wgs84> boundary)
        {
            string path = Path.Combine(Path.GetTempPath(), Guid.NewGuid() + ".kml");

            var sb = new StringBuilder();
            sb.Append("<?xml version=\"1.0\" encoding=\"UTF-8\"?>");
            sb.Append("<kml xmlns=\"http://www.opengis.net/kml/2.2\">");
            sb.Append("<Placemark><name>" + name + "</name>");
            sb.Append("<Polygon><outerBoundaryIs><LinearRing><coordinates>");
            foreach (var p in boundary)
            {
                sb.Append(p.Longitude.ToString("F8", CultureInfo.InvariantCulture));
                sb.Append(",");
                sb.Append(p.Latitude.ToString("F8", CultureInfo.InvariantCulture));
                sb.Append(",0 ");
            }
            sb.Append("</coordinates></LinearRing></outerBoundaryIs></Polygon></Placemark></kml>");

            File.WriteAllText(path, sb.ToString());
            return path;
        }
    }
}
