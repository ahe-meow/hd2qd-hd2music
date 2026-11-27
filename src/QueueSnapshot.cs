namespace Hd2Qd.HD2Music
{
    /// <summary>Saved between manager launches in %LOCALAPPDATA%\HD2Phone\ncm_queue.json.</summary>
    public sealed class QueueSnapshot
    {
        public int Version = 2;

        public string AccountUid = "";

        public string ListId = "";

        public string ListName = "";

        public int Index = -1;

        public int TrackFirst = 1;

        /// <summary>Position within Index and the listener's volume, restored on next launch.</summary>
        public double Pos;

        public int Volume = 65;

        public System.Collections.Generic.List<Track> Tracks = new System.Collections.Generic.List<Track>();
    }
}
