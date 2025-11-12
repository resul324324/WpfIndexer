namespace WpfIndexer.Models
{
    public class UpdateResult
    {
        public List<string> Added { get; } = new();
        public List<string> Updated { get; } = new();
        public List<string> Deleted { get; } = new();
    }
}