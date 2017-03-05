using ImapMigration;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace ImapMigrationConsole
{
    class Program
    {
        static void Main(string[] args)
        {

            using (MigrationTask task = new MigrationTask())
            {
                Console.WriteLine("URL Format is");
                Console.WriteLine("\tscheme://host:port?username=username&password=password&root-folder=INBOX.");
                Console.WriteLine("\t\t\tscheme = imap or imap-ssl");
                Console.WriteLine("\t\t\tport is optional");
                Console.WriteLine("\t\t\troot-folder is optional");
                string url = Prompt("SourceURL");
                

                task.SourceServer = new ServerAddress(url);


                url = Prompt("DestinationURL");

                task.DestinationServer = new ServerAddress(url);

                task.Migrate();

                Console.ReadLine();
            }

        }

        private static string Prompt(string title, bool showCursor = true)
        {

            if (System.IO.File.Exists(title + ".txt"))
                return System.IO.File.ReadAllText(title + ".txt");

            Console.WriteLine(title);
            var cv = Console.CursorVisible;
            Console.CursorVisible = showCursor;
            string line = Console.ReadLine();
            Console.CursorVisible = cv;
            return line;
        }
    }
}
