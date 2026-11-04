using System;
using System.Collections.Generic;
using System.Linq;

namespace Neurocle.Application.Projects
{
    // 提供自動分配資料集時供規劃器使用的圖片、類別及可用狀態。
    public sealed class DatasetSplitCandidate
    {
        /// <summary>
        /// 建立單張圖片的切分候選資料。
        /// </summary>
        /// <param name="imageId">圖片識別碼。</param>
        /// <param name="classIds">圖片目前具有標註的類別識別碼。</param>
        /// <param name="isAvailable">圖片檔案目前是否存在且可讀取。</param>
        /// <exception cref="ArgumentNullException">必要參數為 null。</exception>
        public DatasetSplitCandidate(string imageId, IReadOnlyList<string> classIds, bool isAvailable)
        {
            ImageId = imageId ?? throw new ArgumentNullException(nameof(imageId));
            ClassIds = classIds ?? throw new ArgumentNullException(nameof(classIds));
            IsAvailable = isAvailable;
        }
        // 取得候選圖片的識別碼。
        public string ImageId { get; }
        // 取得候選圖片的標註類別識別碼。
        public IReadOnlyList<string> ClassIds { get; }
        // 取得候選圖片是否可供匯出或訓練使用。
        public bool IsAvailable { get; }
    }

    // 保存自動切分後的訓練圖片、測試圖片及未達成條件的警告。
    public sealed class DatasetSplitPlan
    {
        /// <summary>
        /// 建立資料集切分結果。
        /// </summary>
        /// <param name="trainImageIds">分配至訓練集的圖片識別碼。</param>
        /// <param name="testImageIds">分配至測試集的圖片識別碼。</param>
        /// <param name="warnings">切分限制未能完全滿足時產生的警告代碼。</param>
        public DatasetSplitPlan(IReadOnlyList<string> trainImageIds, IReadOnlyList<string> testImageIds, IReadOnlyList<string> warnings)
        {
            TrainImageIds = trainImageIds; TestImageIds = testImageIds; Warnings = warnings;
        }
        // 取得訓練集圖片識別碼。
        public IReadOnlyList<string> TrainImageIds { get; }
        // 取得測試集圖片識別碼。
        public IReadOnlyList<string> TestImageIds { get; }
        // 取得無法完全滿足測試集或類別最小數量條件的警告代碼。
        public IReadOnlyList<string> Warnings { get; }
    }

    // 依圖片類別分布建立隨機切分，並在可行時保留每類至少十張訓練圖片。
    public static class DatasetSplitPlanner
    {
        /// <summary>
        /// 依指定比例建立切分結果；至少三張可用圖片時，測試集目標數量不低於三張。
        /// </summary>
        /// <param name="values">納入切分評估的圖片候選。</param>
        /// <param name="trainPercent">希望分配至訓練集的百分比，範圍為 1 至 99。</param>
        /// <param name="next">提供排序隨機鍵的函式；每次以 Int32.MaxValue 作為上限呼叫。</param>
        /// <returns>包含訓練集、測試集及限制警告的規劃結果。</returns>
        /// <exception cref="ArgumentNullException"><paramref name="next"/> 或 <paramref name="values"/> 為 null。</exception>
        /// <exception cref="ArgumentOutOfRangeException"><paramref name="trainPercent"/> 不在 1 至 99 之間。</exception>
        /// <exception cref="ArgumentException">可用圖片中存在重複的圖片識別碼。</exception>
        public static DatasetSplitPlan Create(IEnumerable<DatasetSplitCandidate> values, int trainPercent, Func<int, int> next)
        {
            if (trainPercent < 1 || trainPercent > 99) throw new ArgumentOutOfRangeException(nameof(trainPercent));
            if (next == null) throw new ArgumentNullException(nameof(next));
            // 只有仍可讀取的圖片可分配；呼叫端提供的亂數來源決定候選順序。
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
                // 先處理目前測試比例較低的類別，並避免可達十筆的類別低於訓練集保留數。
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
                // 無標註圖片不影響類別平衡，僅用來補足目標測試集大小。
                if (test.Count == testCount) break;
                test.Add(candidate.ImageId);
            }
            var warnings = new List<string>();
            if (test.Count < testCount) warnings.Add("train-minimum");
            if (remainingByClass.Values.Any(value => value < 10)) warnings.Add("class-minimum");
            if (available.Length < 3) warnings.Add("test-minimum");
            return new DatasetSplitPlan(shuffled.Where(value => !test.Contains(value.ImageId)).Select(value => value.ImageId).ToArray(), shuffled.Where(value => test.Contains(value.ImageId)).Select(value => value.ImageId).ToArray(), warnings);
        }

        // 保存單一類別的總數、已選數及待檢查候選圖片。
        private sealed class ClassBucket
        {
            /// <summary>
            /// 建立一個類別的切分統計桶。
            /// </summary>
            /// <param name="classId">類別識別碼。</param>
            /// <param name="total">項目總數。</param>
            /// <param name="candidates">供切分規劃的候選圖片。</param>
            public ClassBucket(string classId, int total, IEnumerable<DatasetSplitCandidate> candidates)
            {
                ClassId = classId; Total = total; Candidates = new Queue<DatasetSplitCandidate>(candidates);
            }
            // 取得此桶代表的類別識別碼。
            public string ClassId { get; }
            // 取得此類別在可用圖片中的總出現數。
            public int Total { get; }
            // 取得已分配至測試集且包含此類別的圖片數。
            public int Selected { get; set; }
            // 取得尚待檢查的候選圖片佇列。
            public Queue<DatasetSplitCandidate> Candidates { get; }
        }

        // 依類別目前選取比例、總量及識別碼建立穩定排序。
        private sealed class ClassBucketComparer : IComparer<ClassBucket>
        {
            /// <summary>
            /// 比較兩個類別的測試集選取比例，並以固定欄位打破平手。
            /// </summary>
            /// <param name="left">排序比較中的第一個類別分桶。</param>
            /// <param name="right">排序比較中的第二個類別分桶。</param>
            /// <returns>依排序規則回傳負數、零或正數。</returns>
            public int Compare(ClassBucket left, ClassBucket right)
            {
                if (ReferenceEquals(left, right)) return 0;
                // 交叉相乘比較選取比例，避免以浮點數排序造成不穩定平手。
                int ratio = ((long)left.Selected * right.Total).CompareTo((long)right.Selected * left.Total);
                if (ratio != 0) return ratio;
                int total = left.Total.CompareTo(right.Total);
                return total != 0 ? total : StringComparer.Ordinal.Compare(left.ClassId, right.ClassId);
            }
        }
    }
}
