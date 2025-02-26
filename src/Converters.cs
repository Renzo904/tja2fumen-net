using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Reflection.Metadata;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading.Tasks;

namespace tja2fumen
{
    public static class Converters
    {
        private static Dictionary<string, List<TJAMeasureProcessed>> ProcessComands(
            Dictionary<string, List<TJAMeasure>> tjaBranches, float bpm)
        {
            /*tja_branches_processed: Dict[str, List[TJAMeasureProcessed]] = {
                branch_name: [] for branch_name in tja_branches.keys()
            }*/

            Dictionary<string, List<TJAMeasureProcessed>> tjaBranchesProcessed =
                new Dictionary<string, List<TJAMeasureProcessed>>();
            foreach (var branchName in tjaBranches.Keys)
            {
                tjaBranchesProcessed.Add(branchName, new List<TJAMeasureProcessed>());
            }

            foreach (var pair in tjaBranches)
            {
                var branchName = pair.Key;
                var branchMeasuresTja = pair.Value;

                float currentBpm = bpm;
                float currentScroll = 1.0f;
                bool currentGoGo = false;
                bool currentBarline = true;
                string currentSenote = "";
                int currentDividend = 4;
                int currentDivisor = 4;

                foreach (var measureTja in branchMeasuresTja)
                {
                    TJAMeasureProcessed measureTjaProcessed = new TJAMeasureProcessed();
                    measureTjaProcessed.bpm = currentBpm;
                    measureTjaProcessed.scroll = currentScroll;
                    measureTjaProcessed.gogo = currentGoGo;
                    measureTjaProcessed.barline = currentBarline;
                    measureTjaProcessed.timeSignature = new List<int>() { currentDividend, currentDivisor };
                    measureTjaProcessed.subDivisions = measureTja.notes.Count();

                    foreach (var data in measureTja.combined)
                    {
                        switch (data.name)
                        {
                        case "note":
                            measureTjaProcessed.notes.Add(data);
                            break;
                        case "delay":
                            measureTjaProcessed.delay = float.Parse(data.value) * 1000;
                            break;
                        case "branch_start":
                            var branchParts = data.value.Split(',');
                            if (branchParts.Length != 3)
                            {
                                throw new Exception(
                                    $"#BRANCHSTART must have 3 comma-separated values, but got '{data.value}' instead.");
                            }
                            string branchType = branchParts[0];
                            string val1 = branchParts[1];
                            string val2 = branchParts[2];
                            (float, float)branchCond;
                            if (branchType.ToLower() == "r")
                            {
                                branchCond = (float.Parse(val1, CultureInfo.InvariantCulture),
                                              float.Parse(val2, CultureInfo.InvariantCulture));
                            }
                            else if (branchType.ToLower() == "p")
                            {
                                branchCond = (float.Parse(val1, CultureInfo.InvariantCulture) / 100,
                                              float.Parse(val2, CultureInfo.InvariantCulture) / 100);
                            }
                            else
                            {
                                throw new Exception($"Invalid #BRANCHSTART type: {branchType}");
                            }
                            measureTjaProcessed.branchType = branchType;
                            measureTjaProcessed.branchCond = branchCond;
                            break;
                        case "section":
                            measureTjaProcessed.section = (data.value != "");
                            break;
                        case "levelhold":
                            measureTjaProcessed.levelHold = true;
                            break;
                        case "barline":
                            currentBarline = int.Parse(data.value) != 0;
                            measureTjaProcessed.barline = currentBarline;
                            break;
                        case "measure":
                            var matchMeasure = Regex.Match(data.value, @"(\d+)/(\d+)");
                            if (!matchMeasure.Success)
                            {
                                continue;
                            }
                            currentDividend = int.Parse(matchMeasure.Groups[1].Value);
                            currentDivisor = int.Parse(matchMeasure.Groups[2].Value);
                            measureTjaProcessed.timeSignature = new List<int> { currentDividend, currentDivisor };
                            break;

                        case "bpm":
                        case "scroll":
                        case "gogo":
                        case "senote":
                            object newVal = 0;

                            switch (data.name)
                            {
                            case "bpm":
                                currentBpm = float.Parse(data.value, CultureInfo.InvariantCulture);
                                newVal = currentBpm;
                                break;
                            case "scroll":
                                currentScroll = float.Parse(data.value, CultureInfo.InvariantCulture);
                                newVal = currentScroll;
                                break;
                            case "gogo":
                                currentGoGo = int.Parse(data.value, CultureInfo.InvariantCulture) != 0;
                                newVal = currentGoGo;
                                break;
                            case "senote":
                                currentSenote = Constants.SENOTECHANGE_TYPES[int.Parse(data.value)];
                                newVal = currentSenote;
                                break;
                            }

                            if (data.pos == 0)
                            {
                                // I hate python
                                switch (data.name)
                                {
                                case "bpm":
                                    measureTjaProcessed.bpm = (float)newVal;
                                    break;
                                case "scroll":
                                    measureTjaProcessed.scroll = (float)newVal;
                                    break;
                                case "gogo":
                                    measureTjaProcessed.gogo = (bool)newVal;
                                    break;
                                case "senote":
                                    measureTjaProcessed.seNote = (string)newVal;
                                    break;
                                }
                            }
                            else
                            {
                                measureTjaProcessed.posEnd = data.pos;
                                tjaBranchesProcessed[branchName].Add(measureTjaProcessed);

                                measureTjaProcessed = new TJAMeasureProcessed();
                                measureTjaProcessed.bpm = currentBpm;
                                measureTjaProcessed.scroll = currentScroll;
                                measureTjaProcessed.gogo = currentGoGo;
                                measureTjaProcessed.barline = currentBarline;
                                measureTjaProcessed.timeSignature = new List<int>() { currentDividend, currentDivisor };
                                measureTjaProcessed.subDivisions = measureTja.notes.Count();
                                measureTjaProcessed.posStart = data.pos;
                                measureTjaProcessed.seNote = currentSenote;
                            }

                            currentSenote = "";

                            break;

                        default:
#if DEBUG
                            Console.WriteLine($"[ Warn ] Unexpected event type: {data.name}");
#endif
                            break;
                        }
                    }
                    measureTjaProcessed.posEnd = measureTja.notes.Count();
                    tjaBranchesProcessed[branchName].Add(measureTjaProcessed);
                }
            }
            bool hasBranches = true;
            foreach (var b in tjaBranchesProcessed.Values)
            {
                if (b.Count() == 0)
                {
                    hasBranches = false;
                }
            }

            if (hasBranches)
            {
                HashSet<int> ints = new HashSet<int>();
                foreach (var b in tjaBranchesProcessed.Values)
                {
                    ints.Add(b.Count());
                };
                if (ints.Count() != 1)
                {
                    throw new Exception("Branches do not have the same number of measures. (This " +
                                        "check was performed after splitting up the measures due " +
                                        "to mid-measure commands. Please check any GOGO, BPMCHANGE, " +
                                        "and SCROLL commands you have in your branches, and make sure " +
                                        "that each branch has the same number of commands.)");
                }
            }

            return tjaBranchesProcessed;
        }

        public static FumenCourse ConvertTjaToFumen(TJACourse tja, bool convertSilently = false)
        {
            var tjaBranchesProcessed = ProcessComands(tja.branches, tja.bpm);

            var nMeasures = tjaBranchesProcessed["normal"].Count();

            FumenCourse fumen = new FumenCourse();
            fumen.hasBranches = tja.hasBranches;
            fumen.measures = new List<FumenMeasure>();
            fumen.diff = tja.course;
            
            for (int i = 0; i < nMeasures; i++)
            {
                fumen.measures.Add(new FumenMeasure());
            }
            fumen.header = new FumenHeader();
            fumen.scoreInit = tja.scoreInit;
            fumen.scoreDiff = tja.scoreDiff;

            fumen.header.b512_b515_number_of_measures = nMeasures;
            fumen.header.b432_b435_has_branches = 1;
            foreach (var b in tjaBranchesProcessed.Values)
            {
                if (b.Count == 0)
                {
                    fumen.header.b432_b435_has_branches = 0;
                }
            }

            var courseBallons = new List<int>(tja.balloon);

            var totalNotes = new Dictionary<string, int> { { "normal", 0 }, { "professional", 0 }, { "master", 0 } };
            var totalBallonsHits = new Dictionary<string, int> { { "normal", 0 }, { "professional", 0 }, { "master", 0 } };

            var totalDrumrollsDuration = new Dictionary<string, float> { { "normal", 0 }, { "professional", 0 }, { "master", 0 } };
            List<string>? branchTypes = null;
            List<(float, float)>? branchConditions = null;

            foreach (var pair in tjaBranchesProcessed)
            {
                var currentBranch = pair.Key;
                var branchTja = pair.Value;

                if (branchTja == null || branchTja.Count == 0)
                {
                    continue;
                }

                var branchPointsTotal = 0;
                var branchPointsMeasure = 0;
                var currentDrumRoll = new FumenNote();
                var currentDrumRollMultiplier = 1;
                var currentLevelhold = false;
                branchTypes = new List<string>();
                branchConditions = new List<(float, float)>();

                for (int idx_m = 0; idx_m < Math.Min(branchTja.Count, fumen.measures.Count); idx_m++)
                {
                    var measureTja = branchTja[idx_m];
                    var measureFumen = fumen.measures[idx_m];
                    measureFumen.branches[currentBranch] = new FumenBranch();
                    var asd = measureFumen.branches[currentBranch];
                    asd.speed = measureTja.scroll;
                    measureFumen.branches[currentBranch] = asd;
                    measureFumen.gogo = measureTja.gogo;
                    measureFumen.bpm = measureTja.bpm;

                    var measureLength = measureTja.posEnd - measureTja.posStart;

                    measureFumen.SetDuration(measureTja.timeSignature, measureLength, measureTja.subDivisions);

                    if (idx_m == 0)
                    {
                        measureFumen.setFirstMsOffsets(tja.offset);
                    }
                    else
                    {
                        measureFumen.setMsOffsets(measureTja.delay, fumen.measures[idx_m - 1]);
                    }

                    bool barlineOff = (measureTja.barline == false);
                    bool isSubMeasure = (measureLength < measureTja.subDivisions && measureTja.posStart != 0);
                    if (barlineOff || isSubMeasure)
                    {
                        measureFumen.barline = false;
                    }

                    var branchType = measureTja.branchType;
                    var branchCond = measureTja.branchCond;

                    if (branchType != "" && branchCond.Item1 != 0.0f && branchCond.Item2 != 0.0f)
                    {
                        measureFumen.setBranchInfo(branchType, branchCond, branchPointsTotal, currentBranch,
                                                   currentLevelhold);

                        branchPointsTotal = 0;
                        currentLevelhold = false;
                        branchTypes.Add(branchType);
                        branchConditions.Add(branchCond);
                    }

                    branchPointsTotal += branchPointsMeasure;

                    if (measureTja.levelHold)
                    {
                        currentLevelhold = true;
                    }

                    branchPointsMeasure = 0;
                    for (int i = 0; i < measureTja.notes.Count; i++)
                    {
                        var noteTja = measureTja.notes[i];
                        

                        float posRatio = ((float)(noteTja.pos - measureTja.posStart) /
                                          (float)(measureTja.posEnd - measureTja.posStart));
                        var notePos = measureFumen.duration * posRatio;

                        if (noteTja.value == "EndDRB")
                        {
                            if (String.IsNullOrEmpty(currentDrumRoll.noteType))
                            {
                                if (!convertSilently)
                                {
                                    Console.WriteLine("[ Warn ] '8' note encountered without matching " +
                                                      "drumroll/balloon/kusudama note. Ignoring to " +
                                                      "avoid crash. Check TJA and re-run.");
                                }
                                continue;
                            }

                            if (!currentDrumRoll.multiMeasure)
                            {
                                currentDrumRoll.duration += notePos - currentDrumRoll.pos;
                            }
                            else
                            {
                                // Alr?
                                currentDrumRoll.duration += notePos - 0.0f;
                            }
                            totalDrumrollsDuration[currentBranch] += (currentDrumRoll.duration * currentDrumRollMultiplier);
                            currentDrumRoll.duration = (float)((int)currentDrumRoll.duration);
                            currentDrumRoll = new FumenNote();
                            continue;
                        }

                        if (noteTja.value == "Kusudama" && currentDrumRoll.noteType != "" &&
                            currentDrumRoll.noteType != null)
                        {
                            continue;
                        }

                        var note = new FumenNote();
                        note.pos = notePos;

                        if (measureTja.seNote != null && measureTja.seNote != "")
                        {
                            note.noteType = measureTja.seNote;
                            note.manuallySet = true;
                            measureTja.seNote = "";
                        }
                        else
                        {
                            note.noteType = noteTja.value;
                        }
                        note.scoreInit = tja.scoreInit;
                        note.scoreDiff = tja.scoreDiff;

                        switch (note.noteType)
                        {
                        case "Drumroll":
                            currentDrumRoll = note;
                            currentDrumRollMultiplier = 1;
                            break;
                        case "DRUMROLL":
                            currentDrumRoll = note;
                            currentDrumRollMultiplier = 2;
                            break;
                        case "Balloon":
                        case "Kusudama":
                            try
                            {
                                note.hits = courseBallons[0];
                                totalBallonsHits[currentBranch] += note.hits;
                                courseBallons.RemoveAt(0);
                            }
                            catch (Exception)
                            {
                                if (!convertSilently)
                                {
                                    Console.WriteLine($"[ Warn ]Not enough values for 'BALLOON:' " +
                                                      $"({tja.balloon}). Using value=1 to " +
                                                      $"avoid crashing. Check TJA and re-run.");
                                }
                                note.hits = 1;
                                totalBallonsHits[currentBranch] += 1;
                                }
                            currentDrumRoll = note;
                            break;
                        default:
                            if (note.noteType.ToLower().StartsWith("don") || note.noteType.ToLower().StartsWith("ka"))
                            {
                                totalNotes[currentBranch] += 1;
                            }
                            break;
                        }
                        int ptsToAdd = 0;
                        switch (note.noteType)
                        {
                        case "Don":
                        case "Ka":
                            ptsToAdd = fumen.header.b468_b471_branch_pts_good;
                            break;
                        case "DON":
                        case "KA":
                            totalNotes[currentBranch] += 1;
                            ptsToAdd = fumen.header.b484_b487_branch_pts_good_big;
                            break;
                        case "Balloon":
                            ptsToAdd = fumen.header.b496_b499_branch_pts_balloon;
                            break;
                        case "Kusudama":
                            ptsToAdd = fumen.header.b500_b503_branch_pts_kusudama;
                            break;
                        default:
                            ptsToAdd = 0;
                            break;
                        }

                        branchPointsMeasure += ptsToAdd;

                        var hateyoupython = measureFumen.branches[currentBranch];
                        hateyoupython.notes.Add(note);
                        hateyoupython.length += 1;
                        measureFumen.branches[currentBranch] = hateyoupython;
                    }

                    if (currentDrumRoll.noteType != "" && currentDrumRoll.noteType != null)
                    {
                        if (currentDrumRoll.multiMeasure)
                        {
                            currentDrumRoll.duration += measureFumen.duration;
                        }
                        else
                        {
                            currentDrumRoll.multiMeasure = true;
                            currentDrumRoll.duration += (measureFumen.duration - currentDrumRoll.pos);
                        }
                    }
                }
            }

            fumen.header.SetHpBytes(totalNotes["normal"], tja.course, tja.level);
            fumen.header.setTimingWindows(tja.course);

            bool drumroll_only = true;
            if (branchTypes == null || branchTypes.Count == 0)
            {
                drumroll_only = false;
            }
            else if (branchConditions == null || branchConditions.Count == 0)
            {
                drumroll_only = false;
            }
            else
            {

                for (int i = 0; i < Math.Min(branchTypes.Count, branchConditions.Count); i++)
                {
                    var branchType = branchTypes[i];
                    var cond = branchConditions[i];
                    if (branchType == "r")
                    {
                        continue;
                    }

                    if (branchType == "p" && cond.Item1 == 0.0f && cond.Item2 == 0.0f)
                    {
                        continue;
                    }

                    if (branchType == "p" && cond.Item1 > 1.0f && cond.Item2 > 1.0f)
                    {
                        continue;
                    }

                    drumroll_only = false;
                    break;
                }
            }

            if (drumroll_only)
            {
                fumen.header.b468_b471_branch_pts_good = 0;
                fumen.header.b484_b487_branch_pts_good_big = 0;
                fumen.header.b472_b475_branch_pts_ok = 0;
                fumen.header.b488_b491_branch_pts_ok_big = 0;
                fumen.header.b496_b499_branch_pts_balloon = 0;
                fumen.header.b500_b503_branch_pts_kusudama = 0;
            }

            bool percentageOnly = branchTypes != null && branchTypes.Count > 0 &&
                                  branchTypes.All(branchType =>
                                                  { return branchType != "r"; });

            if (percentageOnly)
            {
                fumen.header.b480_b483_branch_pts_drumroll = 0;
                fumen.header.b492_b495_branch_pts_drumroll_big = 0;
            }

            if (totalNotes["professional"] != 0)
            {
                fumen.header.b460_b463_normal_professional_ratio =
                    (int)(65536 * (totalNotes["normal"] / totalNotes["professional"]));
            }

            if (totalNotes["master"] != 0)
            {
                fumen.header.b464_b467_normal_master_ratio =
                    (int)(65536 * (totalNotes["normal"] / totalNotes["master"]));
            }

            // For balloons, the last hit that pops the balloon awards 5000 points, and previous hits award 300 apiece.
            var balloonScore = ((totalBallonsHits.Values.Max() - tja.balloon.Count) * 300) + (tja.balloon.Count * 5000);

            // This scaling factor was empirically determined from a few reference files.
            var drumRollScore = ((totalDrumrollsDuration.Values.Max()) * .872 );

            // The linearizedScore is the contribution of hitting notes, which is a function of the total note quantity, which
            // is 1 per small note + 2 per large note.
            var linearizedScore = (1000000.0 - balloonScore - drumRollScore);

            // The total score is the value for each note hit. The rounding machinations ensure that it's the smallest multiple of
            // 10 that results in a score >= 1000000
            var totalScore = linearizedScore / totalNotes.Values.Max();
            fumen.shinuchiScore = (int)(Math.Ceiling(totalScore / 10) * 10);

            return fumen;
        }

        private static List<object> ClusterNotes(List<object> itemList, List<int> clusterDiffs)
        {
            List<object> clusteredNotes = new List<object>();
            List<FumenNote> currentCluster = new List<FumenNote>();

            foreach (object item in itemList)
            {
                if (item is List<FumenNote>)
                {
                    if (currentCluster.Count > 0)
                    {
                        clusteredNotes.Add(currentCluster);
                        currentCluster = new List<FumenNote>();
                    }
                    clusteredNotes.Add(item);
                }
                else
                {
                    if (item is FumenNote)
                    {
                        if (clusterDiffs.Any(diff =>
                                             { return ((FumenNote)item).diff == diff; }))
                        {
                            currentCluster.Add((FumenNote)item);
                        }
                        else
                        {
                            if (currentCluster.Count > 0)
                            {
                                currentCluster.Add((FumenNote)item);
                                clusteredNotes.Add(currentCluster);
                                currentCluster = new List<FumenNote>();
                            }
                            else
                            {
                                clusteredNotes.Add(item);
                            }
                        }
                    }
                }
            }

            if (currentCluster.Count > 0)
            {
                clusteredNotes.Add(currentCluster);
            }
            return clusteredNotes;
        }

        private static void ReplaceAlternateDonKas(List<List<FumenNote>> noteClusters, int eighthNoteDuration)
        {
            var bigNotes = new string[] { "DON", "DON2", "KA", "KA2" };
            foreach (var cluster in noteClusters)
            {
                for (int i = 0; i < cluster.Count; i++)
                {
                    var note = cluster[i];
                    if (!bigNotes.Contains(note.noteType) && !note.manuallySet)
                    {
                        int temp;
                        if (int.TryParse(note.noteType.AsSpan(note.noteType.Length - 1), out temp))
                        {
                            note.noteType = note.noteType.Substring(0, note.noteType.Length - 1) + "2";
                        }
                        else
                        {
                            note.noteType += "2";
                        }
                    }

                    cluster[i] = note;
                }

                bool allDons = cluster.All(note =>
                                           { return note.noteType.StartsWith("Don"); });

                for (int i = 0; i < cluster.Count; i++)
                {
                    var note = cluster[i];
                    if (allDons && (cluster.Count % 2 == 1) && (i % 2 == 1) && !bigNotes.Contains(note.noteType) &&
                        !note.manuallySet)
                    {
                        note.noteType = "Don3";
                    }
                    cluster[i] = note;
                }

                bool isFastClusterOf4 = (cluster.Count == 4 && cluster.All(note =>
                                                                        { return note.diff < eighthNoteDuration; }));

                if (!isFastClusterOf4)
                {
                    if (!bigNotes.Contains(cluster.Last().noteType) && !cluster.Last().manuallySet)
                    {
                        var note = cluster.Last();
                        note.noteType = note.noteType.Substring(0, note.noteType.Count() - 1);
                        cluster[cluster.Count - 1] = note;
                    }
                }
            }
        }

        private static void FixDkNoteTypes(List<FumenNote> dkNotes, float songBpm)
        {
            dkNotes.Sort((note1, note2) => note1.posAbs.CompareTo(note2.posAbs));

            for (int i = 0; i < dkNotes.Count - 1; i++)
            {
                var note = dkNotes[i];
                note.diff = (int)(dkNotes[i + 1].posAbs - dkNotes[i].posAbs);
                dkNotes[i] = note;
            }

            List<int> diffsUnique = new List<int>();
            foreach (var note in dkNotes)
            {
                if (!diffsUnique.Contains(note.diff))
                {
                    diffsUnique.Add(note.diff);
                }
            }
            diffsUnique.Sort();

            var measureDuration = (4 * 60_000) / songBpm;
            int quarterNoteDuration = (int)(measureDuration / 4);
            List<int> diffsUnderQuarter = diffsUnique.Where(diff => diff < quarterNoteDuration).ToList();

            List<List<int>> diffsToCluster = new List<List<int>>();
            List<int> diffsUnder8th = new List<int>();
            int eighthNoteDuration = (int)(measureDuration / 8);

            foreach (var diff in diffsUnderQuarter)
            {
                if (diff < eighthNoteDuration)
                {
                    diffsUnder8th.Add(diff);
                }
                else
                {
                    diffsToCluster.Add(new List<int> { diff });
                }
            }

            if (diffsUnder8th.Count > 0)
            {
                diffsToCluster.Insert(0, diffsUnder8th);
            }

            List<object> semiClustered = new List<object>();
            foreach (var note in dkNotes)
            {
                semiClustered.Add(note);
            }

            foreach (var diffVals in diffsToCluster)
            {
                semiClustered = ClusterNotes(semiClustered, diffVals);
            }

            List<List<FumenNote>> clusteredNotes = new List<List<FumenNote>>();
            foreach (var cluster in semiClustered)
            {
                if (cluster is List<FumenNote>)
                {

                    clusteredNotes.Add((List<FumenNote>)cluster);
                }
                else
                {
                    clusteredNotes.Add(new List<FumenNote> { (FumenNote)cluster });
                }
            }

            ReplaceAlternateDonKas(clusteredNotes, eighthNoteDuration);
        }

        public static void FixDkNoteTypesCourse(FumenCourse fumen)
        {
            List<float> measureBpms = new List<float>();
            foreach (var m in fumen.measures)
            {
                measureBpms.Add(m.bpm);
            }

            // Im gonna be honest, i have no fcking idea how this works
            float songBpm =
                measureBpms.GroupBy(i => i).OrderByDescending(grp => grp.Count()).Select(grp => grp.Key).First();

            foreach (var branchName in Constants.BRANCH_NAMES)
            {
                List<FumenNote> dkNotes = new List<FumenNote>();
                foreach (FumenMeasure measure in fumen.measures)
                {
                    for (int i = 0; i < measure.branches[branchName].notes.Count; i++)
                    {
                        FumenNote note = measure.branches[branchName].notes[i];
                        if (new List<string> { "don", "ka" }.Any(t =>
                                                                 { return note.noteType.ToLower().StartsWith(t); }))
                        {
                            note.posAbs = (measure.offsetStart + note.pos + (4 * 60_000 / measure.bpm));
                            measure.branches[branchName].notes[i] = note;
                            dkNotes.Add(measure.branches[branchName].notes[i]);
                        }
                    }
                }

                if (dkNotes.Count > 0)
                {
                    FixDkNoteTypes(dkNotes, songBpm);
                }
            }
        }
    }
}
