using System;
using System.Linq;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using System.Data.Entity;
using System.Text;
using System.Threading.Tasks;

namespace ImapMigration
{

    [Table("Messages")]
    public class Message {

        [Key,DatabaseGenerated(DatabaseGeneratedOption.Identity)]
        public long ID { get; set; }

        [StringLength(400)]
        [Index]
        public string Url { get; set; }

        [StringLength(64)]
        [Index("FolderHash", Order = 2)]
        public string Hash { get; set; }

        [StringLength(380)]
        [Index("FolderHash", Order = 1)]
        public string Folder { get; set; }

        [StringLength(400)]
        [Index]
        public string MessageID { get; set; }

    }

}