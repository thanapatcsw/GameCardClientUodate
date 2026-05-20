using Postgrest.Attributes;
using Postgrest.Models;
using System;

[Table("matchmaking_queue")]
public class MatchmakingQueueData : BaseModel
{
    [PrimaryKey("id", false)]
    public string Id { get; set; }

    [Column("player_id")]
    public string PlayerId { get; set; }

    [Column("status")]
    public string Status { get; set; }

    [Column("room_code")]
    public string RoomCode { get; set; }

    [Column("room_id")]
    public long? RoomId { get; set; }

    [Column("target_player_count")]
    public int TargetPlayerCount { get; set; }

    [Column("search_request_id")]
    public string SearchRequestId { get; set; }

    [Column("created_at")]
    public DateTime CreatedAt { get; set; }

    [Column("matched_at")]
    public DateTime? MatchedAt { get; set; }
}
