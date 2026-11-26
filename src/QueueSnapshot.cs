namespace Hd2Qd.HD2Music
{
    /// <summary>Saved between manager launches in %LOCALAPPDATA%\HD2Phone\ncm_queue.json.
    /// Playback position is deliberately not saved — only the playlist and the selected row.</summary>
    public sealed class QueueSnapshot
    {
        public int Version = 1;

        public string AccountUid = "";

        public string ListId = "";

        public string ListName = "";

        public int Index = -1;

        public int TrackFirst = 1;

        public System.Collections.Generic.List<Track> Tracks = new System.Collections.Generic.List<Track>();
    }
}
