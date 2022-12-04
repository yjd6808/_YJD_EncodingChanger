using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Forms;

using UtfUnknown;

using static System.Net.Mime.MediaTypeNames;

using DataFormats = System.Windows.DataFormats;
using DragEventArgs = System.Windows.DragEventArgs;
using MessageBox = System.Windows.MessageBox;
using Path = System.IO.Path;

namespace EncodingChanger
{
    public class DisposeAction : IDisposable
    {
        private readonly Action _action;
        public DisposeAction(Action action) => _action = action;
        public void Dispose() { _action();  }
    }

    public class EncodingComboBoxItem
    {
        public string Name { get; set; }
        public Encoding Encoding { get; set; }
    }

    public class LoadedFileListBoxItem
    {
        public string VisiblePath { get; set; }
        public string RealPath { get; set; }
    }

    public partial class MainWindow : Window
    {
        private List<string> _extensionFilter = new();
        private bool _working = false;

        public MainWindow()
        {
            InitializeComponent();
            InitializeUI();
            InitializeFilters();
        }

        private void InitializeUI()
        {
            InitializeComboBox();
            _pgbChanged.Height = 0;
        }

        private void InitializeComboBox()
        {
            // 인코딩 목록
            // https://learn.microsoft.com/en-us/dotnet/api/system.text.encoding?redirectedfrom=MSDN&view=net-7.0

            // UTF 시그니쳐 넣는법
            // https://stackoverflow.com/questions/5266069/streamwriter-and-utf-8-byte-order-marks

            // 다른 코드페이지 추가 방법
            // System.Text.Encoding.CodePages 누겟 설치해줌
            // https://stackoverflow.com/questions/50858209/system-notsupportedexception-no-data-is-available-for-encoding-1252
            Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);

            _cbEncodings.Items.Add(new EncodingComboBoxItem { Name = "UTF-8 With Bom",      Encoding = new UTF8Encoding(true)           });
            _cbEncodings.Items.Add(new EncodingComboBoxItem { Name = "UTF-8",               Encoding = new UTF8Encoding(false)          });
            _cbEncodings.Items.Add(new EncodingComboBoxItem { Name = "UTF-16LE",            Encoding = new UnicodeEncoding(false, true) });
            _cbEncodings.Items.Add(new EncodingComboBoxItem { Name = "UTF-16BE",            Encoding = new UnicodeEncoding(true, true)  });
            _cbEncodings.Items.Add(new EncodingComboBoxItem { Name = "UTF-32LE",            Encoding = new UTF32Encoding(false, true)   });
            _cbEncodings.Items.Add(new EncodingComboBoxItem { Name = "UTF-32BE",            Encoding = new UTF32Encoding(true, true)    });
            _cbEncodings.Items.Add(new EncodingComboBoxItem { Name = "CP949",               Encoding = Encoding.GetEncoding(949)        });
            _cbEncodings.Items.Add(new EncodingComboBoxItem { Name = "EUC-KR",              Encoding = Encoding.GetEncoding(51949)      });
            _cbEncodings.SelectedIndex = 0;
        }


        private void _libFiles_OnDrop(object sender, DragEventArgs e)
        {
            if (e.Data.GetDataPresent(DataFormats.FileDrop))
            {
                string[] files = (string[])e.Data.GetData(DataFormats.FileDrop);

                foreach (var file in files)
                {
                    if ((File.GetAttributes(file) & FileAttributes.Directory) == FileAttributes.Directory)
                    {
                        var subFiles = Directory.EnumerateFiles(file, "*.*", SearchOption.AllDirectories);   // 하위폴더 포함

                        foreach (string subFile in subFiles)
                            FilterAdd(subFile);
                    }
                    else
                    {
                        FilterAdd(file);
                    }
                }

                if (_libFiles.Items.Count > 0)
                    _lbDragDrop.Visibility = Visibility.Hidden;
            }
        }

        private void FilterAdd(string file)
        {
            if (_extensionFilter.Count > 0 && _extensionFilter.FindIndex(s => s == Path.GetExtension(file)) != -1)
                _libFiles.Items.Add(new LoadedFileListBoxItem { RealPath = file, VisiblePath = AbbreviatePath(file) });
            else if (_extensionFilter.Count == 0)
                _libFiles.Items.Add(new LoadedFileListBoxItem { RealPath = file, VisiblePath = AbbreviatePath(file) });
        }

        private string AbbreviatePath(string path, int containParentDirectoryCount = 3, bool root = true)
        {
            List<string> splited = path.Split(@"\").ToList();
            LinkedList<string> builder = new ();

            for (int i = splited.Count - 2, level = 1; i >= 1; --i, ++level)
            {
                if (level > containParentDirectoryCount)
                {
                    builder.AddFirst("..\\");
                    continue;
                }

                builder.AddFirst(splited[i] + "\\");
            }

            if (root) builder.AddFirst(splited[0] + "\\");
            builder.AddLast(splited[^1]);

            return builder.Aggregate(string.Empty, (current, s) => current + s);
        }



        private async Task SaveFileWithEncodingAsync(List<string> files, string targetDirectory = "")
        {
            _working = true;
            using DisposeAction _ = new(() => _working = false);
            int fileCount = files.Count;
            int changedCount = 0;
            Encoding dstEncoding = (_cbEncodings.SelectedItem as EncodingComboBoxItem).Encoding;

            _pgbChanged.Visibility = Visibility.Visible;
            _pgbChanged.Height = 30;
            _lbInfo.Content = $"{changedCount} / {fileCount}";

            StringBuilder log = new(10000);

            foreach (string filePath in files)
            {
                if (!File.Exists(filePath))
                    continue;

                try
                {
                    // 인코딩 변경 방법
                    // https://stackoverflow.com/questions/1922199/c-sharp-convert-string-from-utf-8-to-iso-8859-1-latin1-h
                    // UTF외의 인코딩 감지 라이브러리
                    // https://github.com/CharsetDetector/UTF-unknown
                    // C# 인코딩 변환시 주의사항 관련해서 정리
                    //  1. .NET string은 기본적으로 UTF-16LE 인코딩을 사용 (https://github.com/dotnet/standard/issues/260#issuecomment-290834776)
                    //  2. File.WriteAllText는 기본적으로 UTF-8로 디폴트 저장하도록 함.
                    //  3. File.ReadAllText는 기본적으로 UTF-8로 읽도록 함.
                    //  이를 종합해볼 때 File.ReadAllText시에 기존 문서의 인코딩형식으로 읽어와야한다.
                    //  예를들어 어떤 문서가 CP949로 인코딩되어있다고 하자.
                    //     1. Encoding.GetEncoding(949).GetDecoder()를 사용해서 C# 환경의 UTF-16LE 형식으로 저장
                    //     2. 그리고 UTF-16LE형식의 문자열을 목표한 인코딩으로 변경해서 저장해야한다.
                    //     이렇게 수행해주면 기존 문자들이 깨지지 않고 잘 변환될 것이다.

                    string fileName = Path.GetFileName(filePath);
                    DetectionResult result = CharsetDetector.DetectFromFile(filePath);

                    if (result.Detected == null)
                    {
                        log.Append($"{fileName} 문자셋 인코딩 감지 실패\n");
                        continue;
                    }

                    // 60% 감지율 이하면 변환 못하도록 한다.
                    if (result.Detected.Confidence < 0.6f)
                    {
                        log.Append($"{fileName} 문자셋 인코딩 감지 실패({result.Detected.Confidence.ToString("0.00")}: {result.Detected.EncodingName})\n");
                        continue;
                    }

                    string content = await File.ReadAllTextAsync(filePath, result.Detected.Encoding);
                    if (targetDirectory.Length > 0)
                        await File.WriteAllTextAsync(Path.Combine(targetDirectory, fileName), content, dstEncoding);
                    else
                        await File.WriteAllTextAsync(filePath, content, dstEncoding);

                    ++changedCount;
                    _pgbChanged.Value = changedCount / (double)fileCount * 100;
                    
                    _lbInfo.Content = $"${changedCount} / {fileCount}";

                }
                catch
                {

                }

                _lbInfo.Content = $"{changedCount} / {fileCount}";
            }


            MessageBox.Show($"{fileCount}중 {changedCount}개의 파일을 성공적으로 변환하였습니다. \n\n{log.ToString()}");
            _lbInfo.Content = "";
            _pgbChanged.Visibility = Visibility.Hidden;
            _pgbChanged.Height = 0;
        }

        private List<string>? ToFileList()
        {
            List<string> files = new();

            foreach (object libFilesItem in _libFiles.Items)
            {
                if (libFilesItem is not LoadedFileListBoxItem item)
                    return null;

                files.Add(item.RealPath);
            }

            return files;
        }

        private async void _btnSaveOverwrite_OnClick(object sender, RoutedEventArgs e)
        {
            if (_libFiles.Items.Count == 0)
            {
                MessageBox.Show("먼저 변환할 파일들을 드래그 앤 드랍 해주세요.");
                return;
            }

            List<string>? files = ToFileList();


            if (files == null)
            {
                MessageBox.Show("이럴수 없어요... 프로그램 껏다가 다시 해보세요.");
                return;
            }

            await SaveFileWithEncodingAsync(files);
        }

        private async void _btnSaveAs_OnClick(object sender, RoutedEventArgs e)
        {
            if (_libFiles.Items.Count == 0)
            {
                MessageBox.Show("먼저 변환할 파일들을 드래그 앤 드랍 해주세요.");
                return;
            }

            List<string>? files = ToFileList();
            

            if (files == null)
            {
                MessageBox.Show("이럴수 없어요... 프로그램 껏다가 다시 해보세요.");
                return;
            }

            List<string> onlyFileNames = files.Select(Path.GetFileName).ToList();
            string duplicateFiles = string.Empty;
            onlyFileNames.GroupBy(x => x)
                         .Select(g => new { Value = g.Key, Count = g.Count() })
                         .OrderByDescending(x => x.Count)
                         .ToList()
                         .FindAll(x => x.Count > 1)
                         .ToList()
                         .ForEach(x => duplicateFiles += $"{x.Value}\n");

            if (duplicateFiles != string.Empty)
            {
                MessageBox.Show("중복된 이름의 파일들이 있습니다.\n확인 후 다시 시도해주세요.\n\n" + duplicateFiles);
                return;
            }

            using var fbd = new FolderBrowserDialog();

            if (fbd.ShowDialog() == System.Windows.Forms.DialogResult.OK &&
                !string.IsNullOrWhiteSpace(fbd.SelectedPath))
            {
                await SaveFileWithEncodingAsync(files, fbd.SelectedPath);
                return;
            }

            MessageBox.Show("제대로된 디렉토리를 선택해주세요.");
        }

        private void _btnClear_OnClick(object sender, RoutedEventArgs e)
        {
            _libFiles.Items.Clear();
            _lbDragDrop.Visibility = Visibility.Visible;
        }

        private void _btnApplyFilter_OnClick(object sender, RoutedEventArgs e)
        {
            List<string>? filters = ParseExts(_tbExtentionsFilter.Text);
            if (filters == null)
            {
                MessageBox.Show("파싱에 실패했습니다.\n*.cpp\n*.cs\n*.cc\n와 같은 형식으로 입력해주세요.");
                return;
            }

            _extensionFilter = filters;
            List<string>? originalFiles = ToFileList();

            if (originalFiles != null)
            {
                _libFiles.Items.Clear();

                originalFiles.FindAll(x => _extensionFilter.FindIndex(s => s == Path.GetExtension(x)) != -1)
                    .ToList()
                    .ForEach(x => _libFiles.Items.Add(x));
            }

            MessageBox.Show("필터가 적용되었습니다.");

            File.WriteAllText("ext.txt", _tbExtentionsFilter.Text);
        }

        private void InitializeFilters()
        {
            if (!File.Exists("ext.txt"))
                return;

            string extensionTexts = File.ReadAllText("ext.txt");
            List<string>? filters = ParseExts(extensionTexts);

            if (filters == null)
                return;

            _tbExtentionsFilter.Text = extensionTexts;
            _extensionFilter = filters;
        }

        private List<string>? ParseExts(string texts)
        {
            List<string> splited = texts.Replace("\r", "")
                .Split("\n")
                .ToList()
                .FindAll(s => s.Length > 0)
                .ToList();
            List<string> filters = new();
            int parsedCount = 0;

            foreach (string s in splited)
            {
                string ext = Path.GetExtension(s);
                if (ext.Length <= 0)
                    continue;
                filters.Add(ext);
                parsedCount++;
            }

            if (parsedCount < splited.Count)
                return null;

            return filters;
        }

        private void _libFiles_OnSelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            

            if (_libFiles.Items.Count == 0)
                return;

            if (_libFiles.SelectedIndex == -1)
                return;

            LoadedFileListBoxItem? item = _libFiles.Items[_libFiles.SelectedIndex] as LoadedFileListBoxItem;

            if (item == null)
                return;

            if (_working)
                return;

            DetectionResult result = CharsetDetector.DetectFromFile(item.RealPath);

            if (result.Detected == null)
            {
                _lbInfo.Content = "인코딩 감지 실패";
                return;
            }

            _lbInfo.Content = $"인코딩: {result.Detected.EncodingName}";
            IList<DetectionDetail> allDetails = result.Details;
        }



    }
}

