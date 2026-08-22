using System;
using System.IO;
using System.Text;

namespace HealthAutoArrange.Core
{
    /// <summary>Independent rules-file storage with replacement that does not delete the good file first.</summary>
    public static class RulesFileStore
    {
        public static void Write(string path, UiConfigModel model)
        {
            if (path == null) throw new ArgumentNullException(nameof(path));
            if (model == null) throw new ArgumentNullException(nameof(model));

            var text = UiConfigTextSerializer.Serialize(model);
            var tempPath = path + ".tmp";
            var backupPath = path + ".bak";
            File.WriteAllText(tempPath, text, Encoding.UTF8);

            if (!File.Exists(path))
            {
                File.Move(tempPath, path);
                return;
            }

            try
            {
                if (File.Exists(backupPath)) File.Delete(backupPath);
                File.Replace(tempPath, path, backupPath, true);
                if (File.Exists(backupPath)) File.Delete(backupPath);
            }
            catch (PlatformNotSupportedException)
            {
                File.Copy(tempPath, path, true);
                File.Delete(tempPath);
            }
            catch (IOException)
            {
                // Keep the existing destination in place until the replacement copy succeeds.
                File.Copy(tempPath, path, true);
                File.Delete(tempPath);
            }
        }

        public static UiConfigModel Read(string path)
        {
            if (path == null) throw new ArgumentNullException(nameof(path));
            if (!File.Exists(path)) return null;
            var text = File.ReadAllText(path, Encoding.UTF8);
            return UiConfigTextSerializer.Parse(text);
        }
    }
}
