using System;
using System.Collections.Generic;
using System.Linq;

namespace Neurocle.Application.Projects
{
    public sealed class DatasetSplitCandidate
    {
        public DatasetSplitCandidate(string imageId, IReadOnlyList<string> classIds, bool isAvailable)
        {
            ImageId = imageId ?? throw new ArgumentNullException(nameof(imageId));
            ClassIds = classIds ?? throw new ArgumentNullException(nameof(classIds));
            IsAvailable = isAvailable;
        }
        public string ImageId { get; }
        public IReadOnlyList<string> ClassIds { get; }
        public bool IsAvailable { get; }
    }

    public sealed class DatasetSplitPlan
    {
        public DatasetSplitPlan(IReadOnlyList<string> trainImageIds, IReadOnlyList<string> testImageIds, IReadOnlyList<string> warnings)
        {
            TrainImageIds = trainImageIds; TestImageIds = testImageIds; Warnings = warnings;
        }
        public IReadOnlyList<string> TrainImageIds { get; }
        public IReadOnlyList<string> TestImageIds { get; }
        public IReadOnlyList<string> Warnings { get; }
    }

    public static class DatasetSplitPlanner
    {
        public static DatasetSplitPlan Create(IEnumerable<DatasetSplitCandidate> values, int trainPercent, Func<int, int> next)
        {
            if (trainPercent < 1 || trainPercent > 99) throw new ArgumentOutOfRangeException(nameof(trainPercent));
            if (next == null) throw new ArgumentNullException(nameof(next));
            DatasetSplitCandidate[] available = (values ?? throw new ArgumentNullException(nameof(values))).Where(value => value.IsAvailable).ToArray();
            if (available.Select(value => value.ImageId).Distinct(StringComparer.Ordinal).Count() != available.Length) throw new ArgumentException("An image can only occur once.", nameof(values));
            var shuffled = available.OrderBy(value => next(Int32.MaxValue)).ToList();
            int requestedTest = (int)Math.Round(available.Length * (100 - trainPercent) / 100d, MidpointRounding.AwayFromZero);
            int minimumTest = available.Length >= 3 ? 3 : available.Length;
            int testCount = Math.Max(requestedTest, minimumTest);
            var classesByImage = shuffled.ToDictionary(value => value.ImageId, value => value.ClassIds.Distinct(StringComparer.Ordinal).ToArray(), StringComparer.Ordinal);
            var remainingByClass = classesByImage.Values.SelectMany(value => value).GroupBy(value => value).ToDictionary(group => group.Key, group => group.Count(), StringComparer.Ordinal);
            var candidatesByClass = remainingByClass.Keys.ToDictionary(value => value, value => new List<DatasetSplitCandidate>(), StringComparer.Ordinal);
            foreach (DatasetSplitCandidate candidate in shuffled)
            {
                foreach (string classId in classesByImage[candidate.ImageId]) candidatesByClass[classId].Add(candidate);
            }
            var buckets = remainingByClass.ToDictionary(value => value.Key, value => new ClassBucket(value.Key, value.Value, candidatesByClass[value.Key]), StringComparer.Ordinal);
            var activeBuckets = new SortedSet<ClassBucket>(new ClassBucketComparer());
            foreach (ClassBucket bucket in buckets.Values) activeBuckets.Add(bucket);
            var test = new HashSet<string>(StringComparer.Ordinal);
            while (test.Count < testCount && activeBuckets.Count > 0)
            {
                ClassBucket bucket = activeBuckets.Min;
                activeBuckets.Remove(bucket);
                DatasetSplitCandidate candidate = null;
                while (bucket.Candidates.Count > 0 && candidate == null)
                {
                    DatasetSplitCandidate possible = bucket.Candidates.Dequeue();
                    if (!test.Contains(possible.ImageId) && classesByImage[possible.ImageId].All(classId => buckets[classId].Total < 10 || remainingByClass[classId] > 10)) candidate = possible;
                }
                if (candidate == null) continue;
                test.Add(candidate.ImageId);
                foreach (string classId in classesByImage[candidate.ImageId]) activeBuckets.Remove(buckets[classId]);
                foreach (string classId in classesByImage[candidate.ImageId]) { remainingByClass[classId]--; buckets[classId].Selected++; }
                foreach (string classId in classesByImage[candidate.ImageId]) if (buckets[classId].Candidates.Count > 0) activeBuckets.Add(buckets[classId]);
            }
            foreach (DatasetSplitCandidate candidate in shuffled.Where(value => classesByImage[value.ImageId].Length == 0))
            {
                if (test.Count == testCount) break;
                test.Add(candidate.ImageId);
            }
            var warnings = new List<string>();
            if (test.Count < testCount) warnings.Add("train-minimum");
            if (remainingByClass.Values.Any(value => value < 10)) warnings.Add("class-minimum");
            if (available.Length < 3) warnings.Add("test-minimum");
            return new DatasetSplitPlan(shuffled.Where(value => !test.Contains(value.ImageId)).Select(value => value.ImageId).ToArray(), shuffled.Where(value => test.Contains(value.ImageId)).Select(value => value.ImageId).ToArray(), warnings);
        }

        private sealed class ClassBucket
        {
            public ClassBucket(string classId, int total, IEnumerable<DatasetSplitCandidate> candidates)
            {
                ClassId = classId; Total = total; Candidates = new Queue<DatasetSplitCandidate>(candidates);
            }
            public string ClassId { get; }
            public int Total { get; }
            public int Selected { get; set; }
            public Queue<DatasetSplitCandidate> Candidates { get; }
        }

        private sealed class ClassBucketComparer : IComparer<ClassBucket>
        {
            public int Compare(ClassBucket left, ClassBucket right)
            {
                if (ReferenceEquals(left, right)) return 0;
                int ratio = ((long)left.Selected * right.Total).CompareTo((long)right.Selected * left.Total);
                if (ratio != 0) return ratio;
                int total = left.Total.CompareTo(right.Total);
                return total != 0 ? total : StringComparer.Ordinal.Compare(left.ClassId, right.ClassId);
            }
        }
    }
}
