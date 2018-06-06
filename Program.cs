using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using HtmlAgilityPack;
using System.Net;
using System.IO;
using System.Net.Http;
using ZetaProducerHtmlCompressor;

namespace MetalDocsToAnki
{
    class Program
    {
        static string Minify(string htmlInput)
        {
            return new HtmlContentCompressor().Compress(htmlInput);
        }

        static string UrlToTempDownloadPath(string url)
        {
            return System.IO.Path.Combine(System.IO.Directory.GetCurrentDirectory(), "temp", new Uri(url).AbsolutePath.Substring(1));
        }

        static string UrlToLocalPath(string url)
        {
            return System.IO.Path.Combine(System.IO.Directory.GetCurrentDirectory(), new Uri(url).AbsolutePath.Substring(1));
        }

        static IEnumerable<HtmlNode> Sections(HtmlDocument doc)
        {
            var sections = doc.DocumentNode.SelectNodes("html/body/div[@id='app']/div/main/section[@id='topics']/div/div/section");
            if (sections == null)
            {
                yield break;
            }

            foreach (var section in sections)
            {
                yield return section;
            }
        }

        static IEnumerable<HtmlNode> Topics(HtmlNode section)
        {
            var topics = section.SelectNodes("div/div[@class='contenttable-section-content column large-9 medium-9 small-12']/div[@class='task-topics']/div");
            if (topics == null)
            {
                yield break;
            }

            foreach (var topic in topics)
            {
                yield return topic;
            }
        }

        static System.IO.FileInfo DownloadPage(HtmlWeb web, string url, HashSet<string> visitedURLs)
        {
            System.IO.FileInfo htmlFile = new System.IO.FileInfo(UrlToTempDownloadPath(url) + ".html");
            htmlFile.Directory.Create();

            if (visitedURLs.Contains(url))
            {
                return htmlFile;
            }

            visitedURLs.Add(url);

            Console.WriteLine("Traversing page: " + url);

            // Politely sleep to avoid overflowing Apple's servers.
            System.Threading.Thread.Sleep(2000);
            var doc = web.Load(url);

            var images = doc.DocumentNode.SelectNodes("//img");
            if (images != null)
            {
                foreach (var image in images)
                {
                    string imgSrc = image.GetAttributeValue("src", null);
                    string imgSrcSet = image.GetAttributeValue("srcset", null);
                    if (imgSrcSet.Substring(0, imgSrc.Length) != imgSrc)
                    {
                        throw new Exception("Expected srcset to be the same link as src");
                    }

                    System.IO.FileInfo imgFile = new System.IO.FileInfo(UrlToLocalPath(imgSrc));
                    imgFile.Directory.Create();

                    Console.WriteLine("Downloading image: " + imgSrc);

                    System.Threading.Thread.Sleep(2000);
                    new System.Net.WebClient().DownloadFile(imgSrc, imgFile.FullName);
                }
            }

            foreach (var section in Sections(doc))
            {
                foreach (var topic in Topics(section))
                {
                    var anchor = topic.SelectSingleNode("div/a");
                    var href = anchor.GetAttributeValue("href", null);
                    System.IO.FileInfo downloadedPage = DownloadPage(web, "https://developer.apple.com" + href, visitedURLs);
                }
            }

            doc.Save(htmlFile.FullName);

            return htmlFile;
        }

        static System.IO.FileInfo ProcessPage(string url, List<string> ankiCsvEntries, HashSet<string> visitedURLs)
        {
            var srcHtmlFile = new System.IO.FileInfo(UrlToTempDownloadPath(url) + ".html");
            var dstHtmlFile = new System.IO.FileInfo(UrlToLocalPath(url) + ".html");
            dstHtmlFile.Directory.Create();

            if (visitedURLs.Contains(url))
            {
                return dstHtmlFile;
            }

            visitedURLs.Add(url);

            var doc = new HtmlDocument();
            doc.Load(srcHtmlFile.FullName);

            doc.DocumentNode.SelectSingleNode("html/head").Remove();

            var appSiblings = doc.DocumentNode.SelectSingleNode("html/body").ChildNodes.Where(node => !(node.Name == "div" && node.Id == "app")).ToArray();
            foreach (var appSibling in appSiblings)
            {
                appSibling.Remove();
            }

            var mainSiblings = doc.DocumentNode.SelectSingleNode("html/body/div[@id='app']/div").ChildNodes.Where(node => node.Name != "main").ToArray();
            foreach (var mainSibling in mainSiblings)
            {
                mainSibling.Remove();
            }

            var images = doc.DocumentNode.SelectNodes("//img");
            if (images != null)
            {
                foreach (var image in images)
                {
                    string imgSrc = image.GetAttributeValue("src", null);
                    string imgSrcSet = image.GetAttributeValue("srcset", null);
                    if (imgSrcSet.Substring(0, imgSrc.Length) != imgSrc)
                    {
                        throw new Exception("Expected srcset to be the same link as src");
                    }

                    FileInfo srcImgFile = new FileInfo(UrlToLocalPath(imgSrc));

                    string ankiFriendlyName = "metal_" + new Uri(imgSrc).AbsolutePath.Substring(1).Replace('/', '_');
                    FileInfo dstImgFile = new FileInfo("anki_media/" + ankiFriendlyName);
                    dstImgFile.Directory.Create();

                    File.Copy(srcImgFile.FullName, dstImgFile.FullName, true);

                    image.SetAttributeValue("src", ankiFriendlyName);
                    image.SetAttributeValue("srcset", imgSrcSet.Replace(imgSrc, ankiFriendlyName));
                }
            }

            // reserve an index so it properly shows up in pre-order traversal
            int ankiCsvEntryIndex = -1;
            if (ankiCsvEntries != null)
            {
                ankiCsvEntryIndex = ankiCsvEntries.Count();
                ankiCsvEntries.Add("");
            }

            foreach (var section in Sections(doc))
            {
                foreach (var topic in Topics(section))
                {
                    var anchor = topic.SelectSingleNode("div/a");
                    var href = anchor.GetAttributeValue("href", null);

                    var entriesToPass = ankiCsvEntries;

                    var codeNode = anchor.SelectSingleNode("code");
                    if (codeNode != null)
                    {
                        if (new List<string>{ "func ", "var ", "case ", "static var ", "static let ", "static func ", "class func ", "init", "subscript" }.Contains(codeNode.FirstChild.InnerText))
                        {
                            entriesToPass = null;
                        }
                    }

                    System.IO.FileInfo processedPage = ProcessPage("https://developer.apple.com" + href, entriesToPass, visitedURLs);

                    anchor.SetAttributeValue("href", "https://developer.apple.com" + href);
                }
            }

            doc.DocumentNode.SelectSingleNode("html/body/div[@id='app']/div/main/div[@class='topic-title']/span[@class='eyebrow']")?.Remove();
            doc.DocumentNode.SelectSingleNode("html/body/div[@id='app']/div/main/div[@class='topic-container section-content row']/div[@class='topic-summary column large-3 medium-3 small-12']")?.Remove();
            doc.DocumentNode.SelectSingleNode("html/body/div[@id='app']/div/main/section[@id='see-also']")?.Remove();
            doc.DocumentNode.SelectSingleNode("html/body/div[@id='app']/div/main/div[@class='betainfo section-alt row']")?.Remove();

            var anchors = doc.DocumentNode.SelectNodes("//a");
            if (anchors != null)
            {
                foreach (var anchor in anchors)
                {
                    string href = anchor.GetAttributeValue("href", null);
                    if (href.StartsWith("/documentation/"))
                    {
                        anchor.SetAttributeValue("href", "https://developer.apple.com" + href);
                    }
                }
            }
            
            doc.Save(dstHtmlFile.FullName);

            if (ankiCsvEntryIndex != -1)
            {
                string docAsString = File.ReadAllText(dstHtmlFile.FullName);
                if (docAsString.Contains('\t'))
                {
                    throw new Exception("tab is not a good enough CSV separator!");
                }

                docAsString = Minify(docAsString);
                docAsString = new System.Text.RegularExpressions.Regex("\r?\n").Replace(docAsString, "<br>");

                var topicHeading = doc.DocumentNode.SelectSingleNode("html/body/div[@id='app']/div/main/div[@class='topic-title']/h1[@class='topic-heading']").InnerText;

                var titleOnlyDoc = new HtmlDocument();
                titleOnlyDoc.LoadHtml(docAsString);
                titleOnlyDoc.DocumentNode.SelectSingleNode("html/body/div[@id='app']/div/main/div[@class='topic-container section-content row']/div[@id='topic-content']")?.Remove();
                titleOnlyDoc.DocumentNode.SelectSingleNode("html/body/div[@id='app']/div/main/section[@id='topics']")?.Remove();

                StringBuilder sb = new StringBuilder();
                StringWriter sw = new StringWriter(sb);
                titleOnlyDoc.Save(sw);
                string titleOnlyString = sb.ToString();

                string ankiCsvEntry =
                    topicHeading + "\t" +
                    url + "\t" +
                    titleOnlyString + "\t" +
                    docAsString;

                ankiCsvEntries[ankiCsvEntryIndex] = ankiCsvEntry;
            }

            return dstHtmlFile;
        }

        static void Main(string[] args)
        {
            // Uncomment if you want to download the whole documentation.
            // You only need to do this once. It takes a while, so go have lunch or something.
            // DownloadPage(new HtmlWeb(), "https://developer.apple.com/documentation/metal", new HashSet<string>());

            var ankiCsvEntries = new List<string>();
            ProcessPage("https://developer.apple.com/documentation/metal", ankiCsvEntries, new HashSet<string>());
            System.IO.File.WriteAllLines("MetalAnki.csv", ankiCsvEntries);
        }
    }
}
