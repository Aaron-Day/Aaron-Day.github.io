namespace AaronDay.Models;

public class GameInventory
{
    public List<Game> Games { get; set; } = new();
    public int Count { get; set; }
}
