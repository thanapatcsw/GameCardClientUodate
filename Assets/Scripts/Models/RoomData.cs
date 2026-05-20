using Postgrest.Attributes;
using Postgrest.Models;
using System;

[Table("rooms")]
public class RoomData : BaseModel
{
    [PrimaryKey("id", false)]
    public int Id { get; set; }

    [Column("room_code")]
    public string RoomCode { get; set; }

    [Column("session_name")]
    public string SessionName { get; set; }

    [Column("host_name")]
    public string HostName { get; set; }

    [Column("player_count")]
    public int PlayerCount { get; set; }

    [Column("status")]
    public string Status { get; set; } // "waiting", "playing", "finished"

    [Column("created_at")]
    public DateTime CreatedAt { get; set; }
}
