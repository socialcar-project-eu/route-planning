using System;
using System.Configuration;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Runtime.Serialization;
using System.ComponentModel;
using System.Net;
using log4net;
using log4net.Config;
using System.Net.Mail;

namespace SocialCar.RoutePlanner.WebAPI
{
    [DataContract]
    public class Message
    {
        private static readonly ILog log = LogManager.GetLogger(System.Reflection.MethodBase.GetCurrentMethod().DeclaringType);

        [DataMember(Name = "result")]
        public bool Result = true;

        [DataMember(Name = "error")]
        public ErrorMessage Error = new ErrorMessage { };

        [DataMember(Name = "data")]
        public List<Contracts.Trip> Trips = new List<Contracts.Trip> { };

        [DataMember(Name = "bad")]
        public List<Contracts.Trip> TripsRemoved = new List<Contracts.Trip> { };
    }


    [DataContract]
    public class ErrorMessage
    {
        [DataMember(Name = "code")]
        public string Code;

        [DataMember(Name = "message")]
        public string Message;

        [DataMember(Name = "timestamp_request")]
        public string TimestampRequest;

        [DataMember(Name = "http_request")]
        public string HttpRequest;

        [DataMember(Name = "response_code")]
        public string ResponseCode;

        [DataMember(Name = "response_description")]
        public string ResponseDescription;

        [DataMember(Name = "id_request")]
        public string IdRequest; 

        [DataMember(Name = "http_method")]
        public string HttpMethod;

        [DataMember(Name = "request_type")]
        public string RequestType;

        [DataMember(Name = "ip_client")]
        public string IpClient;

        [DataMember(Name = "user_agent")]
        public string UserAgent;

        [DataMember(Name = "duration")]
        public string Duration;

        [DataMember(Name = "params")]
        public Dictionary<string, string> Params = new Dictionary<string, string> { };

        [DataMember(Name = "rp_message")]
        public RoutePlanner.Message RPMessage = new RoutePlanner.Message { };

        public string GetDescriptionFromEnumValue(ErrorCodes value)
        {
            var type = typeof(ErrorCodes);
            var memInfo = type.GetMember(value.ToString());
            var attributes = memInfo[0].GetCustomAttributes(typeof(DescriptionAttribute), false);
            var description = ((DescriptionAttribute)attributes[0]).Description;

            return description.ToString();
        }

        public string GetDefaultValueAttributeFromEnumValue(ErrorCodes value)
        {
            var type = typeof(ErrorCodes);
            var memInfo = type.GetMember(value.ToString());
            var attributes = memInfo[0].GetCustomAttributes(typeof(DefaultValueAttribute), false);
            var defaultValueAttribute = ((DefaultValueAttribute)attributes[0]).Value;

            return defaultValueAttribute.ToString();
        }

    }

    [DataContract]
    public enum ErrorCodes
    {
        //This error code is used to catch all Exceptions not yet handled
        [DefaultValueAttribute("00000")]
        GENERAL_ERROR,

        [DescriptionAttribute("DeparturePoint cannot be resolved (coordinates outside the site boundaries)")]
        [DefaultValueAttribute("00001")]
        DEPARTURE_POINT,

        [DescriptionAttribute("DestinationPoint cannot be resolved (coordinates outside the site boundaries)")]
        [DefaultValueAttribute("00002")]
        DESTINATION_POINT,

        [DescriptionAttribute("Wrong RequestType")]
        [DefaultValueAttribute("00003")]
        WRONG_REQUEST_TYPE,

        [DescriptionAttribute("RP processing time too long (> 20s)")]
        [DefaultValueAttribute("00004")]
        RP_PROCESSING_TIME_TOO_LONG,
    };

}

