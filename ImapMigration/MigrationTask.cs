using MailKit.Net.Imap;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Text;
using System.Threading.Tasks;
using MailKit;
using MimeKit;
using MailKit.Search;
using System.Security.Cryptography;
using System.Data.SQLite;

namespace ImapMigration
{
    /// <summary>
    /// 
    /// </summary>
    public class MigrationTask: IDisposable
    {
        private ImapClient sourceClient;
        private ImapClient destinationClient;
        private TransferContext transferContext;

        private SHA256 hash;
        private SQLiteConnection conn;

        /// <summary>
        /// 
        /// </summary>
        public ServerAddress SourceServer { get; set; }

        /// <summary>
        /// 
        /// </summary>
        public ServerAddress DestinationServer { get; set; }


        /// <summary>
        /// 
        /// </summary>
        public bool CheckIfMessageExists { get; set; }


        public void Migrate() {

            hash = SHA256.Create();

            System.Data.SQLite.SQLiteConnectionStringBuilder cnstr = new System.Data.SQLite.SQLiteConnectionStringBuilder();
            cnstr.DataSource = "Transfercontext.sdb";
            conn = new System.Data.SQLite.SQLiteConnection(cnstr.ConnectionString);

            transferContext = new TransferContext(conn, true);

            sourceClient = new ImapClient();



            destinationClient = new ImapClient();


            sourceClient.Connect(SourceServer.Server, SourceServer.Port, SourceServer.SSL);
            Console.WriteLine("Source server connected");

            destinationClient.Connect(DestinationServer.Server, DestinationServer.Port, DestinationServer.SSL);
            Console.WriteLine("Destination server connected");


            sourceClient.Authenticate(SourceServer.Username,SourceServer.Password);
            Console.WriteLine("Source server authenticated");

            destinationClient.Authenticate(DestinationServer.Username,DestinationServer.Password);
            Console.WriteLine("Destination server authenticated");

            foreach (var ns in sourceClient.PersonalNamespaces) {

                CopyNamespace(ns);

            }


        }

        private void CopyNamespace(FolderNamespace ns)
        {
            //Console.WriteLine(ns);

            //Console.WriteLine(sourceClient.Inbox.FullName);

            var folders = sourceClient.GetFolders(ns, false);

            Copy(sourceClient.Inbox,false);

            var rootFolder = destinationClient.GetFolder(ns);

            var destFolders = rootFolder.GetSubfolders(false).ToList();

            var allFolders = destinationClient.GetFolders(ns, false);

            foreach (var folder in folders) {
                //Console.WriteLine(folder.FullName);
                Copy(folder, true, destFolders);
            }
        }

        private void Copy(IMailFolder folder, bool create = true, IList<IMailFolder> destFolders = null)
        {
            IMailFolder dest = destinationClient.Inbox;

            if (create)
            {
                dest = GetSubFolder(folder);
                Console.WriteLine($"Folder exists {dest.FullName}");

            }

            CopyMessages(folder, dest);
        }

        private IMailFolder GetSubFolder(IMailFolder folder)
        {
            if (folder.ParentFolder == null) {
                return destinationClient.GetFolder(destinationClient.PersonalNamespaces[0]);
            }

            var p = GetSubFolder(folder.ParentFolder);

            var fs = p.GetSubfolders(false);

            var e = fs.FirstOrDefault(x => x.Name.Equals(folder.Name, StringComparison.OrdinalIgnoreCase));
            if (e != null)
                return e;

            try {
                return p.GetSubfolder(folder.Name);
            } catch {
                Console.WriteLine($"Creating {folder.FullName}");
                return p.Create(folder.Name, true);
            }
        }

        private void CopyMessages(IMailFolder folder, IMailFolder dest)
        {
            try
            {
                folder.Open(FolderAccess.ReadOnly);

                dest.Open(FolderAccess.ReadWrite);

                UniqueIdRange r = new UniqueIdRange(UniqueId.MinValue, UniqueId.MaxValue);

                var headers = new HashSet<HeaderId>();

                headers.Add(HeaderId.Received);
                headers.Add(HeaderId.Date);
                headers.Add(HeaderId.MessageId);
                headers.Add(HeaderId.Subject);
                headers.Add(HeaderId.From);
                headers.Add(HeaderId.To);
                headers.Add(HeaderId.Cc);
                headers.Add(HeaderId.ResentMessageId);


                var msgList = folder.Fetch(r, 
                    MessageSummaryItems.UniqueId
                    | MessageSummaryItems.InternalDate
                    | MessageSummaryItems.Flags, headers);

                int total = msgList.Count;
                int i = 1;

                foreach (var msg in msgList) {
                    Console.WriteLine($"Copying {i++} of {total}");
                    CopyMessage(msg, folder, dest);
                    if (i % 100 == 0) {
                        dest.Check();
                    }
                }

            }
            finally
            {
                folder.Close();
                dest.Close();
            }
        }

        private void CopyMessage(IMessageSummary msg, IMailFolder folder, IMailFolder dest)
        {

            if (MessageExists(msg, folder, dest))
            {
                return;
            }

            var m = folder.GetMessage(msg.UniqueId);



            //string rd = msg.Headers[HeaderId.Received];

            try
            {
                dest.Append(m,
                    msg.Flags == null ? MessageFlags.None : msg.Flags.Value, msg.InternalDate ?? (DateTimeOffset.Now));
            }
            catch (Exception ex) {
                /*if (!ex.Message.Contains("Message too large.")) {
                    throw;
                }*/

                // failed...

                string failedFolder = "failed\\" + folder.FullName.Replace(folder.DirectorySeparator, '-');

                string failed = failedFolder + "\\" + DateTime.UtcNow.Ticks + ".msg";
                string reason = failed + ".reason.txt";

                string dir = System.IO.Path.GetDirectoryName(failed);
                if (!System.IO.Directory.Exists(dir))
                    System.IO.Directory.CreateDirectory(dir);

                using (var fs = System.IO.File.OpenWrite(failed)) {
                    m.WriteTo(fs);
                }

                System.IO.File.WriteAllText(reason, ex.ToString());


            }

            StoreLocal(msg, folder);
            
        }

        private void StoreLocal(IMessageSummary msg, IMailFolder folder)
        {
            using (var tx = new TransferContext(conn, false))
            {
                string mid = msg.Headers[HeaderId.MessageId];
                if (mid != null)
                {
                    tx.Messages.Add(new ImapMigration.Message
                    {
                        Url = DestinationServer.Url,
                        MessageID = mid
                    });
                    tx.SaveChanges();
                    return;
                }

                tx.Messages.Add(new ImapMigration.Message
                {
                    Folder = folder.FullName,
                    Hash = GetHash(msg),
                    Url = DestinationServer.Url
                });
                tx.SaveChanges();
            }
        }

        private bool MessageExists(IMessageSummary msg, IMailFolder folder, IMailFolder dest)
        {
            // check if message exists on dest...
            string mid = msg.Headers[HeaderId.MessageId];

            // check if exists locally only....


            if (MessageExistsLocally(mid, msg, folder))
                return true;

            if (!CheckIfMessageExists)
                return false;


            SearchQuery query = null;

            if (mid != null)
            {
                query = SearchQuery.HeaderContains("Message-ID", mid);
            }
            else
            {
                mid = msg.Headers[HeaderId.ResentMessageId];
                if (mid != null)
                {
                    query = SearchQuery.HeaderContains("Resent-Message-ID", mid);
                }
                else
                {
                    Console.WriteLine($"No message id found for {msg.Headers[HeaderId.Subject]}");
                }
            }

            if (query != null)
            {
                var ids = dest.Search(query);
                if (ids.Count == 1)
                {
                    //Console.WriteLine("Message exists at destination");
                    return true;
                }
            }
            return false;
        }

        private bool MessageExistsLocally(string mid, IMessageSummary msg, IMailFolder folder)
        {

            if (mid != null)
            {
                if (transferContext.Messages.Any(x => x.Url == DestinationServer.Url && x.MessageID == mid))
                    return true;
            }

            string hs = GetHash(msg);

            if (transferContext.Messages.Any(x => x.Url == DestinationServer.Url && x.Folder == folder.FullName && x.Hash == hs))
                return true;



            return false;
        }

        private string GetHash(IMessageSummary msg)
        {
            string all = string.Join("\r\n", msg.Headers.Select(x => x.Field + ": " + x.Value));

            var h = hash.ComputeHash(System.Text.Encoding.UTF8.GetBytes(all));
            var hs = string.Join("", h.Select(x => x.ToString("x2")));
            return hs;
        }

        public void Dispose()
        {
            sourceClient?.Dispose();
            destinationClient?.Dispose();
            transferContext?.Dispose();
            sourceClient = null;
            destinationClient = null;
            transferContext = null;
        }
    }

    public struct ServerAddress {

        public string Server { get; }

        public int Port { get; }

        public bool SSL { get;  }

        public string Username { get;  }

        public string Password { get;  }
        public string RootFolder { get; }

        public string Url { get; set; }

        public ServerAddress(string url)
            :this(new Uri(url))
        {

        }

        public ServerAddress(Uri uri) {
            Url = uri.ToString();
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
