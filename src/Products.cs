using System;
using System.Collections.Generic;
using System.IO;
using System.Text;

namespace AgPath
{
    /// <summary>
    /// Crops (products) and varieties. File products.txt, one product per line:
    /// Chinese cabbage|Produro|Manoko
    /// </summary>
    class Products
    {
        public SortedDictionary<string, List<string>> Items =
            new SortedDictionary<string, List<string>>(StringComparer.CurrentCultureIgnoreCase);

        static string FilePath { get { return Path.Combine(Program.AppDir, "products.txt"); } }
        static string LegacyPath { get { return Path.Combine(Program.AppDir, "tuotteet.txt"); } }

        public static Products Load()
        {
            Products p = new Products();
            try
            {
                string path = File.Exists(FilePath) ? FilePath : LegacyPath;
                if (File.Exists(path))
                {
                    foreach (string raw in File.ReadAllLines(path, Encoding.UTF8))
                    {
                        string line = raw.Trim();
                        if (line == "" || line.StartsWith("#")) continue;
                        string[] parts = line.Split('|');
                        string prod = parts[0].Trim();
                        if (prod == "") continue;
                        for (int i = 1; i < parts.Length; i++) p.Add(prod, parts[i].Trim());
                        p.Add(prod, null);
                    }
                }
            }
            catch (Exception ex) { Log.Write("Reading products: " + ex.Message); }
            return p;
        }

        public void Save()
        {
            try
            {
                StringBuilder sb = new StringBuilder();
                sb.AppendLine("# Product|Variety1|Variety2|...");
                foreach (KeyValuePair<string, List<string>> kv in Items)
                {
                    sb.Append(kv.Key);
                    foreach (string v in kv.Value) sb.Append("|").Append(v);
                    sb.AppendLine();
                }
                File.WriteAllText(FilePath, sb.ToString(), new UTF8Encoding(false));
            }
            catch (Exception ex) { Log.Write("Saving products: " + ex.Message); }
        }

        /// <summary>Adds a product (and variety if given). Returns true if something new was added.</summary>
        public bool Add(string product, string variety)
        {
            if (string.IsNullOrEmpty(product)) return false;
            product = product.Trim().Replace("|", "/");
            bool added = false;
            List<string> list;
            if (!Items.TryGetValue(product, out list))
            {
                list = new List<string>();
                Items[product] = list;
                added = true;
            }
            if (!string.IsNullOrEmpty(variety))
            {
                variety = variety.Trim().Replace("|", "/");
                bool exists = false;
                foreach (string v in list)
                    if (string.Equals(v, variety, StringComparison.CurrentCultureIgnoreCase)) { exists = true; break; }
                if (!exists)
                {
                    list.Add(variety);
                    list.Sort(StringComparer.CurrentCultureIgnoreCase);
                    added = true;
                }
            }
            return added;
        }

        public List<string> Varieties(string product)
        {
            List<string> list;
            if (product != null && Items.TryGetValue(product.Trim(), out list)) return list;
            return new List<string>();
        }
    }
}
