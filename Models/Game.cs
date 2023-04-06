namespace AaronDay.Models;

public class Game : GameBase
{
    public List<string> Mechanics { get; set; } = new();
    public List<string> Categories { get; set; } = new();
    public int MinPlayersBest { get; set; } = -1;
    public int MaxPlayersBest { get; set; } = -1;
    public long BggId { get; set; } = -1;
    public string Upc { get; set; } = string.Empty;
    public List<string> Expansions { get; set; } = new();
    public bool Expanded { get; set; } = false;
    public string ExpandedText => Expanded ? "Show less" : "Show more";
}
