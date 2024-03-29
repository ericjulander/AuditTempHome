// Generated by https://quicktype.io

namespace CanvasCourseObjects.CourseModule
{
    using System;
    using System.Collections.Generic;

    using System.Globalization;
    using Newtonsoft.Json;
    using Newtonsoft.Json.Converters;

    public partial class CanvasModule
    {
        [JsonProperty("id")]
        public long Id { get; set; }

        [JsonProperty("workflow_state")]
        public string WorkflowState { get; set; }

        [JsonProperty("position")]
        public long Position { get; set; }

        [JsonProperty("name")]
        public string Name { get; set; }

        [JsonProperty("unlock_at")]
        public string UnlockAt { get; set; }

        [JsonProperty("require_sequential_progress")]
        public bool RequireSequentialProgress { get; set; }

        [JsonProperty("prerequisite_module_ids")]
        public long[] PrerequisiteModuleIds { get; set; }

        [JsonProperty("items_count")]
        public long ItemsCount { get; set; }

        [JsonProperty("items_url")]
        public Uri ItemsUrl { get; set; }

        [JsonProperty("items")]
        public object Items { get; set; }

        [JsonProperty("state")]
        public string State { get; set; }

        [JsonProperty("completed_at")]
        public object CompletedAt { get; set; }

        [JsonProperty("publish_final_grade")]
        public object PublishFinalGrade { get; set; }

        [JsonProperty("published")]
        public bool Published { get; set; }
    }

    public partial class CanvasModule
    {
        public static CanvasModule FromJson(string json) => JsonConvert.DeserializeObject<CanvasModule>(json, CanvasCourseObjects.CourseModule.Converter.Settings);
    }

    public static class Serialize
    {
        public static string ToJson(this CanvasModule self) => JsonConvert.SerializeObject(self, CanvasCourseObjects.CourseModule.Converter.Settings);
    }

    internal static class Converter
    {
        public static readonly JsonSerializerSettings Settings = new JsonSerializerSettings
        {
            MetadataPropertyHandling = MetadataPropertyHandling.Ignore,
            DateParseHandling = DateParseHandling.None,
            Converters = {
                new IsoDateTimeConverter { DateTimeStyles = DateTimeStyles.AssumeUniversal }
            },
        };
    }
}
