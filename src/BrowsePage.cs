using System.Collections.Generic;

namespace Hd2Qd.HD2Music
{
    /// <summary>One page of a paged browse list. Offset is the next request's start.</summary>
    public sealed class BrowsePage<T>
    {
        public List<T> Rows = new List<T>();

        public int Offset;

        public bool More;
    }
}
