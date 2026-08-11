// COrchardRecorder.cs - orchard row recording from the painted sections (F50, 2026-08-11).
//
// The row is no longer typed in as a lateral/longitudinal offset of a reference point. The
// implement is parked far out to one side, the driver goes down BOTH sides of the row, and the
// two painted strips overlap over it. The row is the middle between the OUTER edges of those
// two strips.
//
// The edges are not reconstructed from the drawn coverage: CalculateSectionLookAhead already
// keeps section[].leftPoint / rightPoint in world space, and the outer edge of the working
// group is the left point of the first section that is painting and the right point of the
// last one. This class samples exactly those two points.
//
// Spec: features/50_orchard-navigation/RECORD_ROW.md
using System;
using System.Collections.Generic;

namespace AgOpenGPS
{
    /// <summary>One drive-by along the row: the two outer edges of the painted strip.</summary>
    public class OrchPass
    {
        public List<vec2> left = new List<vec2>();
        public List<vec2> right = new List<vec2>();

        /// <summary>Strip centre, used for direction and for pairing the two passes.</summary>
        public List<vec2> Mid()
        {
            var m = new List<vec2>(left.Count);
            int n = Math.Min(left.Count, right.Count);
            for (int i = 0; i < n; i++)
                m.Add(new vec2((left[i].easting + right[i].easting) * 0.5,
                               (left[i].northing + right[i].northing) * 0.5));
            return m;
        }

        public double Length()
        {
            var m = Mid();
            double d = 0;
            for (int i = 1; i < m.Count; i++) d += glm.Distance(m[i], m[i - 1]);
            return d;
        }
    }

    /// <summary>A tree the driver marked while recording: where the mark was dropped.</summary>
    public class OrchTreeMark
    {
        public vec2 pos;
        public int passIdx;
    }

    public class COrchardRecorder
    {
        private readonly FormGPS mf;

        // A short accidental switch-off must not split the pass (field call 2026-08-10 20:22).
        // Constants, not settings - they get nudged after the first real orchard, not by the user.
        private const double MergeSeconds = 10.0;
        private const double MergeMetres = 15.0;
        private const double SampleStep = 1.0;    // metres between samples along the pass

        /// <summary>The small recorder window is open - only then is anything sampled.</summary>
        public bool isArmed;

        public readonly List<OrchPass> passes = new List<OrchPass>();
        public readonly List<OrchTreeMark> treeMarks = new List<OrchTreeMark>();

        private OrchPass current;
        private bool wasOn;
        private double lastE, lastN;
        private DateTime offAt;
        private vec2 offPos, offDir;

        // where the painted coverage stood when this row started, so "discard" can take it back
        private readonly List<int> paintStart = new List<int>();
        private int saveStart;

        public COrchardRecorder(FormGPS _f) { mf = _f; }

        public bool HasData => passes.Count > 0 || current != null;
        public int PassCount => passes.Count + (current != null ? 1 : 0);

        /// <summary>Current pass length in metres - shown in the recorder window status line.</summary>
        public double CurrentLength() => current == null ? 0 : current.Length();

        /// <summary>Start a fresh row: drop everything and remember where the paint stands.</summary>
        public void StartRow()
        {
            passes.Clear();
            treeMarks.Clear();
            current = null;
            wasOn = false;
            lastE = double.MaxValue;

            paintStart.Clear();
            if (mf.triStrip != null)
                for (int j = 0; j < mf.triStrip.Count; j++)
                    paintStart.Add(mf.triStrip[j].patchList == null ? 0 : mf.triStrip[j].patchList.Count);
            saveStart = mf.patchSaveList == null ? 0 : mf.patchSaveList.Count;
        }

        /// <summary>Drop the row and take back the coverage it painted (Discard row).</summary>
        public void DiscardRow()
        {
            RemovePaintSinceStart();
            StartRow();
        }

        /// <summary>Row is built - the strips have done their job, wipe them off the map.</summary>
        public void ClearPaint() => RemovePaintSinceStart();

        private void RemovePaintSinceStart()
        {
            if (mf.triStrip != null)
            {
                for (int j = 0; j < mf.triStrip.Count && j < paintStart.Count; j++)
                {
                    var pl = mf.triStrip[j].patchList;
                    if (pl == null) continue;
                    while (pl.Count > paintStart[j]) pl.RemoveAt(pl.Count - 1);
                }
            }
            if (mf.patchSaveList != null)
                while (mf.patchSaveList.Count > saveStart) mf.patchSaveList.RemoveAt(mf.patchSaveList.Count - 1);
        }

        /// <summary>
        /// Called from the position update. Samples the outer edges of whatever the tool is
        /// painting and splits the drive into passes on section ON/OFF.
        /// </summary>
        public void Sample()
        {
            if (!isArmed || mf == null || !mf.isJobStarted) return;

            int first = -1, last = -1;
            for (int j = 0; j < mf.tool.numOfSections; j++)
            {
                if (!mf.section[j].isMappingOn) continue;
                if (first < 0) first = j;
                last = j;
            }

            bool on = first >= 0;

            if (!on)
            {
                if (wasOn)
                {
                    // pass paused - it only really ended if the driver stays off long enough
                    offAt = DateTime.Now;
                    offPos = new vec2(mf.pivotAxlePos.easting, mf.pivotAxlePos.northing);
                    offDir = HeadingVec();
                    wasOn = false;
                }
                return;
            }

            if (!wasOn)
            {
                if (current != null && !ContinuesSamePass()) CloseCurrentPass();
                if (current == null) current = new OrchPass();
                wasOn = true;
                lastE = double.MaxValue;   // do not draw a straight line across the gap
            }

            vec2 l = mf.section[first].leftPoint;
            vec2 r = mf.section[last].rightPoint;
            double ce = (l.easting + r.easting) * 0.5;
            double cn = (l.northing + r.northing) * 0.5;

            if (lastE != double.MaxValue)
            {
                double de = ce - lastE, dn = cn - lastN;
                if (de * de + dn * dn < SampleStep * SampleStep) return;
            }
            lastE = ce; lastN = cn;

            current.left.Add(l);
            current.right.Add(r);
        }

        /// <summary>A short stop in the same direction is the same pass, not a new one.</summary>
        private bool ContinuesSamePass()
        {
            if (current == null) return false;
            if ((DateTime.Now - offAt).TotalSeconds > MergeSeconds) return false;

            vec2 now = new vec2(mf.pivotAxlePos.easting, mf.pivotAxlePos.northing);
            if (glm.Distance(now, offPos) > MergeMetres) return false;

            vec2 d = HeadingVec();
            return (d.easting * offDir.easting + d.northing * offDir.northing) > 0;   // no U-turn
        }

        private vec2 HeadingVec()
        {
            double h = mf.fixHeading;
            return new vec2(Math.Sin(h), Math.Cos(h));
        }

        private void CloseCurrentPass()
        {
            if (current != null && current.left.Count >= 2) passes.Add(current);
            current = null;
        }

        /// <summary>Everything recorded so far, with the pass in progress closed off.</summary>
        public List<OrchPass> FinishedPasses()
        {
            CloseCurrentPass();
            wasOn = false;
            return passes;
        }

        /// <summary>Drop a tree mark at the centre of the painted strip.</summary>
        public void MarkTree()
        {
            if (!isArmed || mf == null || !mf.isJobStarted) return;

            int first = -1, last = -1;
            for (int j = 0; j < mf.tool.numOfSections; j++)
            {
                if (!mf.section[j].isMappingOn) continue;
                if (first < 0) first = j;
                last = j;
            }
            if (first < 0) return;      // nothing is painting - no reference point to mark from

            vec2 l = mf.section[first].leftPoint;
            vec2 r = mf.section[last].rightPoint;
            treeMarks.Add(new OrchTreeMark
            {
                pos = new vec2((l.easting + r.easting) * 0.5, (l.northing + r.northing) * 0.5),
                passIdx = passes.Count
            });
        }

        public void DeleteLastTree()
        {
            if (treeMarks.Count > 0) treeMarks.RemoveAt(treeMarks.Count - 1);
        }
    }
}
