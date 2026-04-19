namespace Kroira.App.Models
{
    public enum FavoriteType { Channel, Movie, Series }

    public class Favorite
    {
        public int Id { get; set; }
        public int ProfileId { get; set; } = 1;
        public FavoriteType ContentType { get; set; }
        public int ContentId { get; set; }
    }
}
