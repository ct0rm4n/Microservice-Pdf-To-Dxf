using netDxf;
using netDxf.Objects;
using PdfSharp.Pdf.IO;
using PdfSharp.Pdf;
using System.Drawing.Imaging;
using netDxf.Units;
using System.Drawing;
using OpenCvSharp;
using PdfSharp.Pdf.Advanced;
using System.Reflection;
using System;
using netDxf.Entities;
using netDxf.Header;

namespace FileManager.Services
{
    public class FileConverter
    {
        public void GetFromImages(string directoryPath, string fileName)
        {
            var listOfOBjects = ConvertPdfToImage(directoryPath, fileName);            
            RunVertical(listOfOBjects, directoryPath);
        }

        public void OpenDocument()
        {
            PdfDocument document = PdfReader.Open("C:\\Users\\usuario\\source\\repos\\FileManager\\export\\Multipage-workorder.pdf");
            int imageCount = 0;
            foreach (PdfPage page in document.Pages)
            {
                PdfDictionary resources = page.Elements.GetDictionary("/Resources");//page.Elements.GetDictionary("/Resources");
                if (resources != null)
                {
                    PdfDictionary xObjects = resources.Elements.GetDictionary("/XObject");
                    if (xObjects != null)
                    {
                        ICollection<PdfItem> items = xObjects.Elements.Values;
                        // Iterate references to external objects
                        foreach (PdfItem item in items)
                        {
                            PdfReference reference = item as PdfReference;
                            if (reference != null)
                            {
                                PdfDictionary xObject = reference.Value as PdfDictionary;
                                // Is external object an image?
                                if (xObject != null && xObject.Elements.GetString("/Subtype") == "/Image")
                                {
                                    ExportImage(xObject, ref imageCount);
                                }
                            }
                        }
                    }
                }
            }
        }
        
        public IDictionary<string, System.Drawing.Image> ConvertPdfToImage(string path, string fileName)
        {
            IDictionary<string, System.Drawing.Image> images = new Dictionary<string, System.Drawing.Image>();
            using (var pdfDocument = PdfiumViewer.PdfDocument.Load(Path.Combine(path, fileName)))
            {
                string tempDir = Path.GetDirectoryName(Assembly.GetExecutingAssembly().FullName);
                var index = 0;
                foreach (var page in pdfDocument.PageSizes)
                {
                    index++;
                    var bitmapImage = pdfDocument.Render(index - 1, 13300, 13300, true);
                    int quality = 100;
                    EncoderParameters encoderParams = new EncoderParameters(1);
                    encoderParams.Param[0] = new EncoderParameter(Encoder.Quality, quality);
                    var tiffEncoder = ImageCodecInfo.GetImageEncoders().First(encoder => encoder.FormatID == ImageFormat.Tiff.Guid);
                    var fileAndPath = System.IO.Path.Combine(path, $"image_page_{index}.Tiff");
                    bitmapImage.Save(fileAndPath, ImageFormat.Tiff);
                    images.Add($"imagepage_{index}.Tiff", bitmapImage);
                }
                pdfDocument.Dispose();
            }
            return images;
        }
        public Bitmap ConvertMet(string filename)
        {
            Bitmap bitmap = new Bitmap(filename);
            return bitmap;
        }

        public static void Run(Options options, string directoryPath)
        {
            Mat image = new Mat(Path.Combine(directoryPath, options.Image));                
            Mat orig = image.Clone();                
            double ratio = image.Height / 500.0;
            image = ImageUtil.Resize(image, height: 900);                
            Mat gray = image.CvtColor(ColorConversionCodes.BGR2GRAY);
            gray = gray.GaussianBlur(new OpenCvSharp.Size(5, 5), 0);
            Mat edged = gray.Canny(75, 200);
            Console.WriteLine("STEP 1: Edge Detection");
            //Cv2.ImShow("Image", image);
            //Cv2.ImShow("Edged", edged);
            //Cv2.WaitKey();
            //Cv2.DestroyAllWindows();

            //find the contours in the edged image, keeping only the
            //largest ones, and initialize the screen contour
            Mat[] cnts;
            using (Mat edgedClone = edged.Clone())
            {
                edgedClone.FindContours(out cnts, new Mat(), RetrievalModes.List, ContourApproximationModes.ApproxSimple);
            }
            Mat screenCnt = null;
            //loop over the contours
            foreach (Mat c in cnts.OrderByDescending(c => c.ContourArea()).Take(5))
            {
                //approximate the contour
                double peri = c.ArcLength(true);
                using (Mat approx = c.ApproxPolyDP(0.02 * peri, true))
                {
                    //if our approximated contour has four points, then we
                    //can assume that we have found our screen
                    if (approx.Rows == 4)
                    {
                        screenCnt = approx.Clone();
                        break;
                    }
                }
            }
            if (screenCnt == null)
            {
                Console.WriteLine("Failed to find polygon with four points");
                return;
            }
            Console.WriteLine("STEP 2: Find contours of paper");
            Cv2.DrawContours(image, new[] { screenCnt }, -1, Scalar.FromRgb(0, 255, 0), 2);
            //Cv2.ImShow("Outline", image);
            //Cv2.WaitKey();
            //Cv2.DestroyAllWindows();

            //apply the four point transform to obtain a top-down
            //view of the original image
            Mat warped = FourPointTransform(orig, screenCnt * ratio);                
            warped = warped.CvtColor(ColorConversionCodes.BGR2GRAY);
            Cv2.AdaptiveThreshold(warped, warped, 251, AdaptiveThresholdTypes.GaussianC, ThresholdTypes.Binary, 251, 10);
            Console.WriteLine("STEP 3: Apply perspective transform");
            Mat origResized = ImageUtil.Resize(orig, height: 950);
            //Cv2.ImShow("Original", origResized);
            Mat warpedResized = ImageUtil.Resize(warped, height: 950);
            //Cv2.ImShow("Scanned", warpedResized);
            //Cv2.WaitKey();
            //Cv2.DestroyAllWindows();
            //var linesPont = new List<LineSegmentPoint>();
            string layerName = $"Page_{options.PageIndex}";
            int startY = -1;
            int startX = -1;
            List<netDxf.Entities.Line> lines = new List<netDxf.Entities.Line>();
            double scale = 1.0 / 33000.0;
            int imageHeight = edged.Height;
            for (int y = 0; y < edged.Height; y++)
            {
                for (int x = 0; x < edged.Width; x++)
                {
                    if (edged.At<byte>(y, x) == 255)
                    {
                        if (startY == -1)
                        {
                            startY = y;
                        }
                        if (startX == -1)
                        {
                            startX = x;
                        }
                    }
                    else if (startY != -1)
                    {
                        int endY = y - 1;
                        Vector2 startPoint = new Vector2(x * scale, (imageHeight - startY) * scale);
                        Vector2 endPoint = new Vector2(x * scale, (imageHeight - startY) * scale);
                        netDxf.Entities.Line dxfLine = new netDxf.Entities.Line(startPoint, endPoint);
                        lines.Add(dxfLine);
                        startY = -1;
                    }
                    else if (startX != -1)
                    {
                        int endX = x - 1;
                        Vector2 startPoint = new Vector2(startX * scale, (imageHeight - y) * scale);
                        Vector2 endPoint = new Vector2(endX * scale, (imageHeight - y) * scale);
                        netDxf.Entities.Line dxfLine = new netDxf.Entities.Line(startPoint, endPoint);
                        lines.Add(dxfLine);
                        startX = -1;
                    }
                }
            }

            using (var ms = new MemoryStream())
            {
                IEnumerable<string> supportFolders = new List<string>() {
                    "export/tmp",
                    "export"
                };
                DxfDocument doc = new DxfDocument();
                doc.AddEntity(lines);
                directoryPath = System.IO.Path.Combine(directoryPath, "dxf");
                var name  = System.IO.Path.Combine(directoryPath, $"image_page_{options.PageIndex}.dxf");
                doc.Save(name);
                ms.Close();
                ms.Dispose();
            }
        }
        public static void RunVertical(IDictionary<string, System.Drawing.Image> list, string directoryPath)
        {
            DxfDocument doc = new DxfDocument();
            var count = 0;
            double currentPositionY = 0;
            List<netDxf.Entities.Line> lines = new List<netDxf.Entities.Line>();
            foreach (var page in list)
            {
                count = count + 1;
                var options = new Options { Image = $"image_page_{count}.Tiff", PageIndex = count };
                Mat image = new Mat(Path.Combine(directoryPath, options.Image));
                Mat orig = image.Clone();
                double ratio = image.Height / 500.0;
                image = ImageUtil.Resize(image, height: 900);
                Mat gray = image.CvtColor(ColorConversionCodes.BGR2GRAY);
                gray = gray.GaussianBlur(new OpenCvSharp.Size(5, 5), 0);
                Mat edged = gray.Canny(75, 200);
                Mat[] cnts;
                using (Mat edgedClone = edged.Clone())
                {
                    edgedClone.FindContours(out cnts, new Mat(), RetrievalModes.List, ContourApproximationModes.ApproxSimple);
                }
                Mat screenCnt = null;
                //loop over the contours
                foreach (Mat c in cnts.OrderByDescending(c => c.ContourArea()).Take(5))
                {
                    //approximate the contour
                    double peri = c.ArcLength(true);
                    using (Mat approx = c.ApproxPolyDP(0.02 * peri, true))
                    {
                        //if our approximated contour has four points, then we
                        //can assume that we have found our screen
                        if (approx.Rows == 4)
                        {
                            screenCnt = approx.Clone();
                            break;
                        }
                    }
                }
                if (screenCnt == null)
                {
                    Console.WriteLine("Failed to find polygon with four points");
                    return;
                }
                Console.WriteLine("STEP 2: Find contours of paper");
                Cv2.DrawContours(image, new[] { screenCnt }, -1, Scalar.FromRgb(0, 255, 0), 2);
                Mat warped = FourPointTransform(orig, (screenCnt * ratio));
                warped = warped.CvtColor(ColorConversionCodes.BGR2GRAY);
                Cv2.AdaptiveThreshold(warped, warped, 251, AdaptiveThresholdTypes.GaussianC, ThresholdTypes.Binary, 251, 10);
                Console.WriteLine("STEP 3: Apply perspective transform");
                Mat origResized = ImageUtil.Resize(orig, height: 950);
                Mat warpedResized = ImageUtil.Resize(warped, height: 950);
                string layerName = $"Page_{options.PageIndex}";
                int startY = -1;
                int startX = -1;                
                double scale = 1.0 / 33000.0;
                int imageHeight = edged.Height;
                
                for (int y = 0; y < edged.Height; y++)
                {
                    for (int x = 0; x < edged.Width; x++)
                    {

                        if (edged.At<byte>(y, x) == 255)
                        {
                            if (startY == -1)
                            {
                                startY = - y;
                            }
                            if (startX == -1)
                            {
                                startX = x;
                            }
                        }
                        else if (startY != -1)
                        {
                            int endY = y - 1;
                            Vector2 startPoint = new Vector2(startX * scale, (startY - Convert.ToInt32(currentPositionY)) * scale);
                            Vector2 endPoint = new Vector2(x * scale, (startY - Convert.ToInt32(currentPositionY)) * scale);
                            netDxf.Entities.Line dxfLine = new netDxf.Entities.Line(startPoint, endPoint);
                            lines.Add(dxfLine);
                            startY = -1;
                            startX = -1;
                        }
                    }
                }


                //Atualiza a posição vertical da próxima página
                currentPositionY += 950;
            }
            doc.AddEntity(lines);
            using (var ms = new MemoryStream())
            {
                directoryPath = System.IO.Path.Combine(directoryPath, "dxf");
                var name = System.IO.Path.Combine(directoryPath, $"image_page_777.dxf");
                doc.Save(name);
                ms.Close();
                ms.Dispose();
            }
            
        }
        private static Mat FourPointTransform(Mat image, Mat pts)
        {
            //obtain a consistent order of the points and unpack them
            //individually
            Tuple<Point2f, Point2f, Point2f, Point2f> orderedPoints = OrderPoints(pts);
            Point2f tl = orderedPoints.Item1, tr = orderedPoints.Item2, br = orderedPoints.Item3, bl = orderedPoints.Item4;

            //compute the width of the new image, which will be the
            //maximum distance between bottom-right and bottom-left
            //x-coordiates or the top-right and top-left x-coordinates
            double widthA = Point2f.Distance(bl, br);
            double widthB = Point2f.Distance(tl, tr);
            int maxWidth = Math.Max((int)widthA, (int)widthB);


            //compute the height of the new image, which will be the
            //maximum distance between the top-right and bottom-right
            //y-coordinates or the top-left and bottom-left y-coordinates
            double heightA = Point2f.Distance(tr, br);
            double heightB = Point2f.Distance(tl, bl);
            int maxHeight = Math.Max((int)heightA, (int)heightB);

            //now that we have the dimensions of the new image, construct
            //the set of destination points to obtain a "birds eye view",
            //(i.e. top-down view) of the image, again specifying points
            //in the top-left, top-right, bottom-right, and bottom-left
            //order
            var dst = new[]
            {
                new Point2f(0,0),
                new Point2f(maxWidth - 1, 0),
                new Point2f(maxWidth - 1, maxHeight - 1),
                new Point2f(0, maxHeight - 1),
            };

            //compute the perspective transform matrix and then apply it
            using (Mat m = Cv2.GetPerspectiveTransform(new[] { tl, tr, br, bl }, dst))
            {
                Mat warped = image.WarpPerspective(m, new OpenCvSharp.Size(maxWidth, maxHeight));
                return warped;
            }
        }

       
        private static Tuple<Point2f, Point2f, Point2f, Point2f> OrderPoints(Mat pts)
        {
            System.Drawing.Point p1 = pts.Get<System.Drawing.Point>(0);
            System.Drawing.Point p2 = pts.Get<System.Drawing.Point>(1);
            System.Drawing.Point p3 = pts.Get<System.Drawing.Point>(2);
            System.Drawing.Point p4 = pts.Get<System.Drawing.Point>(3);
            Point2f[] points =
            {
                new Point2f(p1.X, p1.Y),
                new Point2f(p2.X, p2.Y),
                new Point2f(p3.X, p3.Y),
                new Point2f(p4.X, p4.Y)
            };

            //sort the points based on their x-coordinates
            Point2f[] xSorted = points.OrderBy(pt => pt.X).ToArray();

            //grab the left-most and right-most points from the sorted
            //x-roodinate points
            Point2f[] leftMost = xSorted.Take(2).ToArray();
            Point2f[] rightMost = xSorted.Skip(2).ToArray();

            //now, sort the left-most coordinates according to their
            //y-coordinates so we can grab the top-left and bottom-left
            //points, respectively
            Point2f tl = leftMost.OrderBy(pt => pt.Y).First();
            Point2f bl = xSorted.OrderBy(pt => pt.Y).Last();

            //now that we have the top-left coordinate, use it as an
            //anchor to calculate the Euclidean distance between the
            //top-left and right-most points; by the Pythagorean
            //theorem, the point with the largest distance will be
            //our bottom-right point
            Point2f[] d = rightMost.OrderBy(pt => tl.DistanceTo(pt)).ToArray();
            Point2f tr = d.First();
            Point2f br = d.Last();

            //return the coordinates in top-left, top-right,
            //bottom-right, and bottom-left order
            return Tuple.Create(tl, tr, br, bl);
        }
        public class Options
        {
            public string Image { get; set; }
            public int PageIndex { get; set; }
        }

        //old tools
        public void GenerateDxf(IDictionary<string, System.Drawing.Image> list)
        {
            var index = 0;

            using (var ms = new MemoryStream())
            {
                DxfDocument doc = new DxfDocument();
                //foreach (var page in list.FirstOrDefault())
                //{
                var page = list.FirstOrDefault();
                index++;
                //var imgSrc = ConvertMet($"image_page_{index}.Tiff");
                var src = new Mat($"image_page_{index}.Tiff", ImreadModes.Color);// BitmapConverter.ToMat(imgSrc);
                Mat dst = new Mat();
                Cv2.Canny(src, dst, 50, 200);
                LineSegmentPoint[] lines1 = Cv2.HoughLinesP(dst, 3, Math.PI, 1, 5, 2);
                List<netDxf.Entities.Line> lines = new List<netDxf.Entities.Line>();
                for (int y = 0; y < dst.Height; y++)
                {
                    for (int x = 0; x < dst.Width; x++)
                    {
                        if (dst.At<byte>(y, x) == 255)
                        {
                            lines.Add(new netDxf.Entities.Line(new Vector2(x, y), new Vector2(x + 1, y + 1)));
                        }
                    }
                }
                doc.AddEntity(lines);
                doc.Save($"image_page_{index}.dxf");
            }
        }

        public void GenerateDxf1(IDictionary<string, System.Drawing.Image> list)
        {
            var index = 0;

            using (var ms = new MemoryStream())
            {
                DxfDocument doc = new DxfDocument();
                //foreach (var page in list.FirstOrDefault())
                //{
                var page = list.FirstOrDefault();
                index++;
                var imgSrc = ConvertMet($"image_page_{index}.Tiff");
                var src = new Mat($"image_page_{index}.Tiff", ImreadModes.Color);// BitmapConverter.ToMat(imgSrc);
                Mat dst = new Mat();
                Cv2.Canny(src, dst, 50, 200);
                LineSegmentPoint[] lines1 = Cv2.HoughLinesP(dst, 3, Math.PI, 1, 5, 2);
                List<netDxf.Entities.Line> lines = new List<netDxf.Entities.Line>();
                for (int y = 0; y < dst.Height; y++)
                {
                    for (int x = 0; x < dst.Width; x++)
                    {
                        if (dst.At<byte>(y, x) == 255)
                        {
                            lines.Add(new netDxf.Entities.Line(new Vector2(x, y), new Vector2(x + 1, y + 1)));
                        }
                    }
                }
                doc.AddEntity(lines);
                doc.Save($"image_page_{index}.dxf");
            }
        }

        public void GenerateDxj(IDictionary<string, System.Drawing.Image> list)
        {
            var index = 0;

            using (var ms = new MemoryStream())
            {
                DxfDocument doc = new DxfDocument();
                //foreach (var page in list.FirstOrDefault())
                //{
                var page = list.FirstOrDefault();
                index++;
                var imgSrc = ConvertMet($"image_page_{index}.Tiff");
                var src = new Mat($"image_page_{index}.Tiff", ImreadModes.Color);// BitmapConverter.ToMat(imgSrc);
                Mat dst = new Mat();
                Cv2.Canny(src, dst, 50, 200);
                LineSegmentPoint[] lines1 = Cv2.HoughLinesP(dst, 3, Math.PI, 1, 5, 2);
                List<netDxf.Entities.Line> lines = new List<netDxf.Entities.Line>();
                for (int y = 0; y < dst.Height; y++)
                {
                    for (int x = 0; x < dst.Width; x++)
                    {
                        if (dst.At<byte>(y, x) == 255)
                        {
                            lines.Add(new netDxf.Entities.Line(new Vector2(x, y), new Vector2(x + 1, y + 1)));
                        }
                    }
                }
                doc.AddEntity(lines);
                doc.Save($"image_page_{index}.dxf");
            }
        }
        public void GetFromImages1()
        {
            string imagePath = "0002.jpg";
            System.Drawing.Image image = System.Drawing.Image.FromFile(imagePath);
            ImageDefinition imageDef = new ImageDefinition(imagePath);
            imageDef.ResolutionUnits = ImageResolutionUnits.Centimeters;
            double width = image.Width;
            double height = image.Height;
            netDxf.Entities.Image dxfImage = new netDxf.Entities.Image(imageDef, new Vector2(0, 0), width, height);
            double x = width / 4;
            double y = height / 4;
            ClippingBoundary clip = new ClippingBoundary(x, y, 2 * x, 2 * y);
            dxfImage.ClippingBoundary = clip;
            DxfDocument doc = new DxfDocument();
            doc.AddEntity(dxfImage);
            doc.Save("teste6.dxf");
        }
        static void ExportImage(PdfDictionary image, ref int count)
        {
            string filter = image.Elements.GetName("/Filter");
            switch (filter)
            {
                case "/DCTDecode":
                    ExportJpegImage(image, ref count);
                    break;

                case "/FlateDecode":
                    ExportAsPngImage(image, ref count);
                    break;
            }
        }
        static void ExportJpegImage(PdfDictionary image, ref int count)
        {
            // Fortunately JPEG has native support in PDF and exporting an image is just writing the stream to a file.
            byte[] stream = image.Stream.Value;
            FileStream fs = new FileStream(String.Format("Image{0}.jpeg", count++), FileMode.Create, FileAccess.Write);
            BinaryWriter bw = new BinaryWriter(fs);
            bw.Write(stream);
            bw.Close();
        }
        static void ExportAsPngImage(PdfDictionary image, ref int count)
        {
            int width = image.Elements.GetInteger(PdfImage.Keys.Width);
            int height = image.Elements.GetInteger(PdfImage.Keys.Height);
            int bitsPerComponent = image.Elements.GetInteger(PdfImage.Keys.BitsPerComponent);
        }
    }
}