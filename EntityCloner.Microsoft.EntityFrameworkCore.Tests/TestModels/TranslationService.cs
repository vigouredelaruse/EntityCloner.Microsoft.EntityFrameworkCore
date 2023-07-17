using EntityCloner.Microsoft.EntityFrameworkCore.Tests.TestModels;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace EntityCloner.Microsoft.EntityFrameworkCore.Benchmarks.TestModels
{
    public class TranslationService
    {
        public int Id { get; set; }
        public byte[] RowVersion { get; set; }
        public string? ServiceName { get; set; }
        public ICollection<Article> Articles { get; set; } = new HashSet<Article>();
    }
}
