using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.ComponentModel;
using log4net;
using log4net.Config;
using System.Runtime.Serialization;

namespace SocialCar.RoutePlanner
{
    [DataContract]
    public class Message
    {
        [DataMember(Name = "error")]
        public List<RPCodes> RoutePlannerCodes = new List<RPCodes>();

        public string GetDescriptionFromEnumValue(RPCodes value)
        {
            var type = typeof(RPCodes);
            var memInfo = type.GetMember(value.ToString());
            var attributes = memInfo[0].GetCustomAttributes(typeof(DescriptionAttribute), false);
            var description = ((DescriptionAttribute)attributes[0]).Description;

            return description.ToString();
        }

        public string GetDefaultValueAttributeFromEnumValue(RPCodes value)
        {
            var type = typeof(RPCodes);
            var memInfo = type.GetMember(value.ToString());
            var attributes = memInfo[0].GetCustomAttributes(typeof(DefaultValueAttribute), false);
            var defaultValueAttribute = ((DefaultValueAttribute)attributes[0]).Value;

            return defaultValueAttribute.ToString();
        }

    }

    [DataContract]
    public enum RPCodes
    {
        [DescriptionAttribute("The construct path fix has been triggered")]
        [DefaultValueAttribute("RP0001")]
        INFO_CONSTRUCT_PATH_FIX_TRIGGERED,

    };
}
