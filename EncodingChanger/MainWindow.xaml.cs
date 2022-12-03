using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection.Metadata.Ecma335;
using System.Text;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Documents;
using System.Windows.Forms;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Navigation;
using System.Windows.Shapes;

using static System.Net.Mime.MediaTypeNames;

using DataFormats = System.Windows.DataFormats;
using DragEventArgs = System.Windows.DragEventArgs;
using MessageBox = System.Windows.MessageBox;
using Path = System.IO.Path;

namespace EncodingChanger
{


    public class EncodingComboBoxItem
    {
        public string Name { get; set; }
        public Encoding Encoding { get; set; }
    }

    public partial class MainWindow : Window
    {
        private List<string> _extensionFilter = new();

        public MainWindow()
        {
            InitializeComponent();
            InitializeComboBox();
            InitializeFilters();
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
                        {
                            FilterAdd(subFile);
                        }
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
                _libFiles.Items.Add(file);
            else if (_extensionFilter.Count == 0)
                _libFiles.Items.Add(file);
        }

        private async Task SaveFileWithEncodingAsync(List<string> files, string targetDirectory = "")
        {
            int fileCount = files.Count;
            int changedCount = 0;
            Encoding encoding = (_cbEncodings.SelectedItem as EncodingComboBoxItem).Encoding;

            _pgbChanged.Visibility = Visibility.Visible;
            _lbChangedProgress.Visibility = Visibility.Visible;
            _lbChangedProgress.Content = $"{changedCount} / {fileCount}";

            foreach (string filePath in files)
            {
                if (!File.Exists(filePath))
                    continue;

                try
                {
                    string readText = await File.ReadAllTextAsync(filePath);

                    if (targetDirectory.Length > 0)
                        await File.WriteAllTextAsync(Path.Combine(targetDirectory, Path.GetFileName(filePath)), readText, encoding);
                    else
                        await File.WriteAllTextAsync(filePath, readText, encoding);

                    ++changedCount;
                    _pgbChanged.Value = changedCount / (double)fileCount * 100;
                    _lbChangedProgress.Content = $"${changedCount} / {fileCount}";
                }
                catch
                {

                }

                _lbChangedProgress.Content = $"{changedCount} / {fileCount}";
            }


            MessageBox.Show($"{fileCount}중 {changedCount}개의 파일을 성공적으로 변환하였습니다.");
            _lbChangedProgress.Visibility = Visibility.Hidden;
            _pgbChanged.Visibility = Visibility.Hidden;
        }

        private List<string>? ToFileList()
        {
            List<string> files = new();

            foreach (object libFilesItem in _libFiles.Items)
            {
                if (libFilesItem is not string filePath)
                    return null;

                files.Add(filePath);
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
    }
}

