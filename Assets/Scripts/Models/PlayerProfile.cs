using Postgrest.Attributes;
using Postgrest.Models;
using System.Collections.Generic;

[Table("player_profiles")]
public class PlayerProfile : BaseModel
{
    [PrimaryKey("id", false)]
    public string Id { get; set; }

    [Column("username")]
    public string Username { get; set; }

    [Column("gems")]
    public int Gems { get; set; }

    [Column("coins")]
    public int Coins { get; set; }

    [Column("mmr")]
    public int Mmr { get; set; }

    [Column("wins")]
    public int Wins { get; set; }

    [Column("losses")]
    public int Losses { get; set; }

    [Column("owned_frames")]
    public List<string> OwnedFrames { get; set; } = new List<string> { "frame_default" };

    [Column("equipped_frame")]
    public string EquippedFrame { get; set; } = "frame_default";

    [Column("selected_character")]
    public int SelectedCharacter { get; set; } = 0;

    [Column("updated_at")]
    public System.DateTime UpdatedAt { get; set; }
}
