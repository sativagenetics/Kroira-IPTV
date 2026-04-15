namespace Kroira.App.Models
{
    public enum FavoriteType { Channel, Movie, Series }

    public class Favorite
    {
        public int Id { get; set; }
        public FavoriteType ContentType { get; set; }
        public int ContentId { get; set; }
    }
}
