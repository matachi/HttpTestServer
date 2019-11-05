using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Drawing;
using System.Drawing.Imaging;
using System.IO;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Runtime.CompilerServices;
using System.Text;
using System.Threading.Tasks;
using System.Windows.Media.Imaging;
using ICSharpCode.AvalonEdit.Document;
using JetBrains.Annotations;

namespace HttpTestServer
{
    public partial class MainWindow : INotifyPropertyChanged
    {
        private readonly HttpListener _httpListener;
        private Attachment _selectedAttachment;
        private Request _selectedItem;

        public MainWindow()
        {
            InitializeComponent();
            DataContext = this;
            _httpListener = new HttpListener();
            _httpListener.Prefixes.Add("http://*:9191/");
            Task.Run(Start);
        }

        public ICollection<Request> Items { get; } = new ObservableCollection<Request>();

        public Request SelectedItem
        {
            get => _selectedItem;
            set
            {
                if (Equals(value, _selectedItem)) return;
                _selectedItem = value;
                SelectedAttachment = null;
                SelectedItemContent.Text = SelectedItem?.Content;
                OnPropertyChanged();
            }
        }

        public TextDocument SelectedItemContent { get; } = new TextDocument();

        public Attachment SelectedAttachment
        {
            get => _selectedAttachment;
            set
            {
                if (Equals(value, _selectedAttachment)) return;
                _selectedAttachment = value;
                SelectedAttachmentContent.Text = Encoding.UTF8.GetString(SelectedAttachment?.Content ?? new byte[] { });
                OnPropertyChanged();
            }
        }

        public TextDocument SelectedAttachmentContent { get; } = new TextDocument();

        public event PropertyChangedEventHandler PropertyChanged;

        public async Task Start()
        {
            _httpListener.Start();
            while (true)
            {
                var context = await _httpListener.GetContextAsync();

                var request = new Request
                {
                    Timestamp = DateTime.Now,
                    Method = context.Request.HttpMethod,
                    Type = context.Request.ContentType,
                    Url = context.Request.Url.ToString()
                };

                if (context.Request.ContentType != null)
                {
                    byte[] content;
                    using (var a = new MemoryStream())
                    {
                        context.Request.InputStream.CopyTo(a);
                        content = a.ToArray();
                    }

                    request.Content = Encoding.UTF8.GetString(content);

                    if (context.Request.ContentType.StartsWith("multipart/"))
                        await ParseFiles(content, context.Request.ContentType,
                            (fileName, name, contentType, stream) =>
                            {
                                byte[] attachmentContent;
                                using (var memoryStream = new MemoryStream())
                                {
                                    stream.CopyTo(memoryStream);
                                    attachmentContent = memoryStream.ToArray();
                                }

                                request.Attachments.Add(new Attachment
                                {
                                    Type = contentType,
                                    FileName = fileName,
                                    Name = name,
                                    Content = attachmentContent
                                });
                            });
                }

                Dispatcher.Invoke(() => Items.Add(request));

                using (var sw = new StreamWriter(context.Response.OutputStream))
                {
                    await sw.FlushAsync();
                }
            }
        }

        public static async Task ParseFiles(byte[] content, string contentType,
            Action<string, string, string, Stream> fileProcessor)
        {
            var streamContent = new StreamContent(new MemoryStream(content));
            streamContent.Headers.ContentType = MediaTypeHeaderValue.Parse(contentType);

            var provider = await streamContent.ReadAsMultipartAsync();

            foreach (var httpContent in provider.Contents)
            {
                var fileName = httpContent.Headers.ContentDisposition.FileName;
                if (string.IsNullOrWhiteSpace(fileName))
                    continue;
                var fileContentType = httpContent.Headers.ContentType.MediaType;
                var name = httpContent.Headers.ContentDisposition.Name;
                using (var fileContents = await httpContent.ReadAsStreamAsync())
                {
                    fileProcessor(fileName, name, fileContentType, fileContents);
                }
            }
        }

        [NotifyPropertyChangedInvocator]
        protected virtual void OnPropertyChanged([CallerMemberName] string propertyName = null)
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
        }

        public class Request
        {
            public DateTime Timestamp { get; set; }
            public string Method { get; set; }
            public string Type { get; set; }
            public IList<Attachment> Attachments { get; } = new List<Attachment>();
            public string Content { get; set; }
            public string Url { get; set; }
        }

        public class Attachment
        {
            public string Type { get; set; }
            public string FileName { get; set; }
            public string Name { get; set; }
            public byte[] Content { get; set; }
            public bool IsImage => Type.StartsWith("image/");
            public bool IsText => !IsImage;

            public BitmapImage Image
            {
                get
                {
                    using (var inputStream = new MemoryStream(Content))
                    {
                        try
                        {
                            using (var bitmap = new Bitmap(inputStream))
                            {
                                return BitmapToImageSource(bitmap);
                            }
                        }
                        catch (ArgumentException)
                        {
                            return null;
                        }
                    }
                }
            }

            private static BitmapImage BitmapToImageSource(Image bitmap)
            {
                using (var memory = new MemoryStream())
                {
                    bitmap.Save(memory, ImageFormat.Bmp);
                    memory.Position = 0;
                    var bitmapImage = new BitmapImage();
                    bitmapImage.BeginInit();
                    bitmapImage.StreamSource = memory;
                    bitmapImage.CacheOption = BitmapCacheOption.OnLoad;
                    bitmapImage.EndInit();
                    return bitmapImage;
                }
            }
        }
    }
}
