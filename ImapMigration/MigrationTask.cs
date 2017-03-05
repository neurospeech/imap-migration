using MailKit.Net.Imap;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Text;
using System.Threading.Tasks;
using MailKit;

namespace ImapMigration
{
    /// <summary>
    /// 
    /// </summary>
    public class MigrationTask: IDisposable
    {
        private ImapClient sourceClient;
        private ImapClient destinationClient;

        /// <summary>
        /// 
        /// </summary>
        public ServerAddress SourceServer { get; set; }

        /// <summary>
        /// 
        /// </summary>
        public ServerAddress DestinationServer { get; set; }



        public void Migrate() {


            sourceClient = new ImapClient();



            destinationClient = new ImapClient();


            sourceClient.Connect(SourceServer.Server, SourceServer.Port, SourceServer.SSL);

            destinationClient.Connect(DestinationServer.Server, DestinationServer.Port, DestinationServer.SSL);


            sourceClient.Authenticate(SourceServer.Username,SourceServer.Password);

            destinationClient.Authenticate(DestinationServer.Username,DestinationServer.Password);

            foreach (var ns in sourceClient.PersonalNamespaces) {

                CopyNamespace(ns);

            }


        }

        private void CopyNamespace(FolderNamespace ns)
        {
            Console.WriteLine(ns);


            foreach (var folder in sourceClient.GetFolders(ns, false)) {
                Console.WriteLine(folder.Name);
            }
        }

        public void Dispose()
        {
            sourceClient?.Dispose();
            destinationClient?.Dispose();
            sourceClient = null;
            destinationClient = null;
        }
    }

    public struct ServerAddress {

        public string Server { get; }

        public int Port { get; }

        public bool SSL { get;  }

        public string Username { get;  }

        public string Password { get;  }
        public object RootFolder { get; }

        public ServerAddress(string url)
            :this(new Uri(url))
        {

        }

        public ServerAddress(Uri uri) {

            Server = uri.Host;
            Port = uri.Port;
            SSL = uri.Scheme.ToLower().Contains("ssl");
            if (Port <= 0)
            {
                Port = SSL ? 993 : 143;
            }
            Username = null;
            Password = null;
            RootFolder = null;
            if (uri.Query == null)
                return;
            var qs = uri.Query.StartsWith("?") ? uri.Query.Substring(1).Split('&') : uri.Query.Split('&');
            
            foreach (var q in qs) {
                var tokens = q.Split(new char[] { '=' }, 2);
                string t1 = tokens[0];
                if (tokens.Length == 1)
                    continue;
                if (t1.Equals("username", StringComparison.OrdinalIgnoreCase)) {
                    Username = tokens[1];
                }
                if (t1.Equals("password", StringComparison.OrdinalIgnoreCase))
                {
                    Password = tokens[1];
                }
                if (t1.Equals("rootfolder", StringComparison.OrdinalIgnoreCase)
                    || t1.Equals("root-folder", StringComparison.OrdinalIgnoreCase)){
                    RootFolder = tokens[1];
                }
            }
        }


    }
}
