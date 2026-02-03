namespace AgenticRpg.Core.Helpers;

public static class ListExtensions
{
    extension<T>(List<T> source)
    {
        public List<List<T>> SplitListBy(int chunkSize)
        {
            ArgumentNullException.ThrowIfNull(source);
            if (chunkSize <= 0) throw new ArgumentException("Chunk size must be greater than 0.", nameof(chunkSize));

            var result = new List<List<T>>();
            for (var i = 0; i < source.Count; i += chunkSize)
            {
                result.Add(source.GetRange(i, Math.Min(chunkSize, source.Count - i)));
            }
            return result;
        }

        public List<T> Shuffle()
        {
            // Fisher-Yates shuffle algorithm
            var rng = new Random();
            var n = source.Count;
            while (n > 1)
            {
                var k = rng.Next(n--);
                (source[n], source[k]) = (source[k], source[n]);
            }
            return source;
        }
    }
}