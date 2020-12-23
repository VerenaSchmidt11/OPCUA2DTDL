﻿using System.Collections.Generic;
using Newtonsoft.Json;

namespace OPCUA2DTDL.Models
{

    public class DtdlContents
    {
        [JsonProperty("@type")]
        public string Type { get; set; }

        [JsonProperty("name")]
        public string Name { get; set; }

        [JsonProperty("target", NullValueHandling = NullValueHandling.Ignore)]
        public string Target { get; set; }

        [JsonProperty("comment", NullValueHandling = NullValueHandling.Ignore)]
        public string Comment { get; set; }

        [JsonProperty("displayName", NullValueHandling = NullValueHandling.Ignore)]
        public string DisplayName { get; set; }

        [JsonProperty("schema", NullValueHandling = NullValueHandling.Ignore)]
        public string Schema { get; set; }
    }

    public class DtdlInterface
    {
        private string _id;

        [JsonProperty("@id")]
        public string Id
        {
            get { return _id; }
            set
            {
                if (value.Length < 128) // DTMI must be < 128 characters
                {
                    _id = value;
                }
            }
        }

        [JsonProperty("@type")]
        public string Type { get; set; }
        
        [JsonProperty("contents", NullValueHandling = NullValueHandling.Ignore)]
        public List<DtdlContents> Contents { get; set; }

        [JsonProperty("@context")]
        public string Context { get; } = "dtmi:dtdl:context;2";

        [JsonProperty("extends", NullValueHandling = NullValueHandling.Ignore)]

        public List<string> Extends { get; set; }

        [JsonProperty("displayName", NullValueHandling = NullValueHandling.Ignore)]
        public string DisplayName { get; set; }

        [JsonProperty("comment", NullValueHandling = NullValueHandling.Ignore)]
        public string Comment { get; set; }
    }
}
