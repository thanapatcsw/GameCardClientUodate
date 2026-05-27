/// <summary>
/// แหล่งรวมค่าเชื่อมต่อ Supabase ที่เดียว (Single Source of Truth)
///
/// หมายเหตุความปลอดภัย: Anon Key เป็นคีย์ฝั่ง client ที่เปิดเผยได้ตามดีไซน์ของ Supabase
/// แต่ "ปลอดภัยก็ต่อเมื่อเปิด Row Level Security (RLS)" บนทุกตารางในฐานข้อมูลแล้วเท่านั้น
/// ถ้ายังไม่เปิด RLS ใครก็อ่าน/เขียนข้อมูลได้ด้วยคีย์นี้
/// </summary>
public static class SupabaseConfig
{
    public const string Url = "https://uwspzhwvjpkcjpoqgkhp.supabase.co";

    public const string AnonKey =
        "eyJhbGciOiJIUzI1NiIsInR5cCI6IkpXVCJ9.eyJpc3MiOiJzdXBhYmFzZSIsInJlZiI6InV3c3B6aHd2anBrY2pwb3Fna2hwIiwicm9sZSI6ImFub24iLCJpYXQiOjE3NzQ4MzUwMzYsImV4cCI6MjA5MDQxMTAzNn0.hgTN21pBcTD2meqXxKnydit0U7inI3OpMOAFVy9NtEE";
}
