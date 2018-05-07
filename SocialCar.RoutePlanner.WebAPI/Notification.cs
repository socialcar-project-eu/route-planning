using System;
using System.Configuration;
using System.Runtime.Serialization;
using System.ComponentModel;
using log4net;
using System.Net.Mail;


namespace SocialCar.RoutePlanner.WebAPI
{
    public class SmtpClientEx : SmtpClient
    {
        private void SetClient(string client)
        {
            typeof(SmtpClient).GetField("clientDomain", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic).SetValue(this, client);
        }

        public SmtpClientEx()
            : base() { }
        public SmtpClientEx(string client)
            : base()
        {
            SetClient(client);
        }
        public SmtpClientEx(string host, string client)
            : base(host)
        {
            SetClient(client);
        }
        public SmtpClientEx(string host, int port, string client)
            : base(host, port)
        {
            SetClient(client);
        }
    }

    public class Notification
    {
        private static readonly ILog log = LogManager.GetLogger(System.Reflection.MethodBase.GetCurrentMethod().DeclaringType);
        public static string[] emails = ConfigurationManager.AppSettings["email_notification"].Split(',');

        public static void SendMessageByEmail(AlarmType alarm, string messageBody, int statusCode = -1)
        {
            bool doit = true;
            if ((emails.Length == 1) && (emails[0] == ""))
                doit = false;

            if (doit)
            {
                //string from = "socialcar_" + Environment.MachineName + "@xxx.xx";
                string from = "socialcar_" + ConfigurationManager.AppSettings["host"] + "@xxx.xx";
                string statusCodeSubject = "";

                MailMessage message = new MailMessage();
                message.From = new MailAddress(from);

                foreach (string email in emails)
                    message.To.Add(email);

                /* Add http status code, if provided */
                if (statusCode != -1)
                    statusCodeSubject = "_" + statusCode.ToString();

                message.Subject = "***RoutePlanner_Alert_Notification_" + GetDescriptionFromEnumValue(alarm) + statusCodeSubject + "***";
                message.Body = messageBody;
                message.DeliveryNotificationOptions = DeliveryNotificationOptions.OnFailure;

                SmtpClientEx smtpClient = new SmtpClientEx("xxx.xxx.xx", 25, "xxx.xxx.xx");
                smtpClient.EnableSsl = false;
                smtpClient.DeliveryMethod = SmtpDeliveryMethod.Network;
                smtpClient.UseDefaultCredentials = true;

                try
                {
                    smtpClient.Send(message);
                }
                catch (Exception ex)
                {
                    log.Error("Exception caught in SendEmail(): " + ex.ToString());
                }
            }
        }
        
        public static string GetDescriptionFromEnumValue(AlarmType alarm)
        {
            var type = typeof(AlarmType);
            var memInfo = type.GetMember(alarm.ToString());
            var attributes = memInfo[0].GetCustomAttributes(typeof(DescriptionAttribute), false);
            var description = ((DescriptionAttribute)attributes[0]).Description;

            return description.ToString();

        }


        [DataContract]
        public enum AlarmType
        {
            [DescriptionAttribute("DEBUG")]
            DEBUG,

            [DescriptionAttribute("INFO")]
            INFO,

            [DescriptionAttribute("WARNING")]
            WARNING,

            [DescriptionAttribute("ERROR")]
            ERROR,

            [DescriptionAttribute("TIME")]
            TIME
        };


    }


}
