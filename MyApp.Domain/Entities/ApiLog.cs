namespace MyApp.Domain.Entities
{
    public class ApiLog
    {
        public int Id { get; set; }
        public string Api { get; set; }
        public long Duration { get; set; }   // ms
        public DateTime Timestamp { get; set; }
    }
}
