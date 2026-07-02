using System;
using System.Text.RegularExpressions;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Media;

namespace LbpArchiveToolkit.Utils
{
    public static partial class RichTextHelper
    {
        [GeneratedRegex(@"(\@[a-zA-Z0-9_-]+)")]
        private static partial Regex MentionRegex();

        public static void SetDescriptionRichText(RichTextBox txtDescription, string? text, Action<string> onMentionClick)
        {
            txtDescription.IsDocumentEnabled = true;
            txtDescription.Document.Blocks.Clear();
            if (string.IsNullOrEmpty(text)) return;

            int lastIndex = 0;
            FlowDocument doc = txtDescription.Document;
            Paragraph para = new Paragraph();

            foreach (var match in MentionRegex().EnumerateMatches(text))
            {
                if (match.Index > lastIndex)
                {
                    para.Inlines.Add(new Run(text.Substring(lastIndex, match.Index - lastIndex)));
                }

                string mentionStr = text.Substring(match.Index, match.Length);
                Hyperlink link = new Hyperlink(new Run(mentionStr))
                {
                    Foreground = Brushes.LightBlue,
                    Cursor = Cursors.Hand
                };

                link.Click += (s, e) =>
                {
                    onMentionClick(mentionStr.Substring(1));
                    e.Handled = true;
                };
                para.Inlines.Add(link);
                lastIndex = match.Index + match.Length;
            }

            if (lastIndex < text.Length)
            {
                para.Inlines.Add(new Run(text.Substring(lastIndex)));
            }

            doc.Blocks.Add(para);
        }
    }
}