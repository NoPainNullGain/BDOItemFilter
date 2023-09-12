using System;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Numerics;
using System.Reflection;
using System.Threading;
using BotCoreProxy;
using OpenCvSharp;
using OpenCvSharp.Extensions;

namespace ItemFilter
{
    // https://stackoverflow.com/questions/32737420/multiple-results-in-opencvsharp3-matchtemplate
    class Program
    {
        public static string[] ItemsToBeRemoved;

        private static Assembly MyResolveEventHandler(object sender, ResolveEventArgs args)
        {
            if (args.Name.Contains("OpenCvSharp.Extensions"))
                return Assembly.Load(File.ReadAllBytes("OpenCvSharp.Extensions.dll"));
            else if (args.Name.Contains("OpenCvSharp"))
                return Assembly.Load(File.ReadAllBytes("OpenCvSharp.dll"));
            else if (args.Name.Contains("System.Drawing.Common"))
                return Assembly.Load(File.ReadAllBytes("System.Drawing.Common.dll"));
            else
                return null;
        }

        public static void Main()
        {
            // Create folder structure in root if not already there
            if (!Directory.Exists("ItemFilter\\RemoveItems"))
                Directory.CreateDirectory("ItemFilter\\RemoveItems");

            // finds the items to remove, you have defined in the folder
            ItemsToBeRemoved = Directory.GetFiles("ItemFilter\\RemoveItems", "*");

            // uncomment only if you are doing work in launcher
            AppDomain.CurrentDomain.AssemblyResolve += MyResolveEventHandler;

            HookManager.AddInternalFunctionCallBack(InternalHooks.Post_RepairWithCamp, typeof(Program).GetMethod("Post_RepairWithCampHook"));
        }

        public static bool Post_RepairWithCampHook(object[] args, out object funcResult)
        {
            try
            {
                while (!Util.CheckInventory())
                {
                    // remember to open inventory then close it and reopen it, cause otherwise you will get ring effect on items that are new since last time you had the inventory open.
                    Input.KeyPress(Keys.I);
                    Thread.Sleep(200);

                    Util.CleanScreen();
                    Thread.Sleep(200);

                    Input.KeyPress(Keys.I);
                    Thread.Sleep(200);
                }

                var x = Convert.ToInt32(ExternalConfig.Resolution.Split('x')[0]);
                var y = Convert.ToInt32(ExternalConfig.Resolution.Split('x')[1]);

                // for each inventory section it scrolls through
                for (var s = 0; s < 3; s++)
                {
                    // for each item you have taken a snapshot of to be removed.
                    foreach (var item in ItemsToBeRemoved)
                    {
                        // fix for diff resolutions the bot supports
                        using (Bitmap inventoryImg = ImageWorker.GetScreenshot(0, 0, x - 135, y))
                        {
                            // Crop image to remove the item count on the image
                            var imageToRemove = (Bitmap)Image.FromFile(item);
                            Rectangle section = new Rectangle(new System.Drawing.Point(0, 0), new System.Drawing.Size(imageToRemove.Width, imageToRemove.Height / 2));
                            Bitmap CroppedImage = CropImage(imageToRemove, section);

                            // find each item in inventory to filter away
                            var found = RunTemplateMatch(inventoryImg, CroppedImage);

                            CroppedImage.Dispose();
                            foreach (OpenCvSharp.Point p in found)
                            {
                                OpenCvSharp.Point corrItemPos;
                                corrItemPos.X = p.X + 20;
                                corrItemPos.Y = p.Y + 20;

                                // item move function
                                MoveItem(corrItemPos);
                            }
                        }
                    }

                    if (s < 2)
                    {
                        Mouse.MoveMouse(
                            new System.Drawing.Point(
                                (ExternalConfig.Resolution.StartsWith("1920") ? 1850 : 2500) - 200,
                                (ExternalConfig.Resolution.StartsWith("1920") ? 800 : 970) - 200),
                            TimeSpan.FromMilliseconds(150));

                        Thread.Sleep(200);

                        for (var k = 0; k < 8; k++)
                        {
                            Input.MouseScrollDown();
                            Thread.Sleep(100);
                        }
                    }
                    else
                    {
                        Util.CleanScreen();
                    }
                }
            }
            catch { }

            Util.CleanScreen();
            funcResult = null;

            return true;
        }

        public static Bitmap CropImage(Bitmap source, Rectangle section)
        {
            var bitmap = new Bitmap(section.Width, section.Height);
            using (var g = Graphics.FromImage(bitmap))
            {
                g.DrawImage(source, 0, 0, section, GraphicsUnit.Pixel);
                return bitmap;
            }
        }

        private static void MoveItem(OpenCvSharp.Point itemPos)
        {
            var newItemPos = ConvertToDrawingPoint(itemPos);

            // check if inventory is open if not, open it.
            while (!Util.CheckInventory())
            {
                Input.KeyPress(Keys.I);
                Thread.Sleep(200);
            }

            // check if cursor is hidden
            if (!Mouse.CursorPresents())
            {
                // press ctrl to activate cursor if its hidden
                Mouse.SetCursor();
            }

            //takes args Point for moving and duration in TimeSpan
            TimeSpan span = TimeSpan.FromMilliseconds(200);
            var res = false;

            Mouse.MoveMouse(newItemPos, span);
            Thread.Sleep(500);
            Mouse.MouseDownL();
            Thread.Sleep(100); // game need some ms to activate ui element

            Mouse.MoveMouse(new System.Drawing.Point((ExternalConfig.Resolution.StartsWith("1920") ? 1850 : 2500), (ExternalConfig.Resolution.StartsWith("1920") ? 800 : 970)), TimeSpan.FromMilliseconds(150));
            Thread.Sleep(300);
            Mouse.MouseUpL();
            Thread.Sleep(200);

            //click to trashbin
            Mouse.MouseClickL(new System.Drawing.Point((ExternalConfig.Resolution.StartsWith("1920") ? 1850 : 2500), (ExternalConfig.Resolution.StartsWith("1920") ? 800 : 970)));
            Thread.Sleep(500);

            //check drop dialog
            var bmp = ExternalConfig.Resolution.StartsWith("1920") ? ImageWorker.GetScreenshot(1700, 640, 1701, 641) : ImageWorker.GetScreenshot(2500, 975, 2501, 976);
            res = ExternalConfig.Resolution.StartsWith("1920") ? ImageWorker.TrueSameColors(bmp.GetPixel(0, 0), Color.FromArgb(112, 88, 54)) : ImageWorker.TrueSameColors(bmp.GetPixel(0, 0), Color.FromArgb(104, 82, 53));

            if (res)//drop dialog presents
            {
                Input.KeyPress(Keys.F);

                Thread.Sleep(200);

                Input.KeyPress(Keys.SPACE, 200);
            }

            var p = Mouse.GetCursorPosition();

            p.Y += 200;
            Mouse.MoveMouse(p, TimeSpan.FromMilliseconds(150));

            Input.KeyPress(Keys.SPACE, 200);
        }

        private static System.Drawing.Point ConvertToDrawingPoint(OpenCvSharp.Point itemPos)
        {
            System.Drawing.Point movePoint = new System.Drawing.Point();

            var vectorPoint = ToVector2(itemPos);
            movePoint.X = (int)vectorPoint.X;
            movePoint.Y = (int)vectorPoint.Y;

            return movePoint;
        }

        private static Vector2 ToVector2(OpenCvSharp.Point point)
        {
            return new Vector2(point.X, point.Y);
        }

        private static List<OpenCvSharp.Point> RunTemplateMatch(Bitmap reference, Bitmap template)
        {
            var refMat = reference.ToMat();
            var tplMat = template.ToMat();
            List<OpenCvSharp.Point> found = new List<OpenCvSharp.Point>();

            using (Mat res = new Mat(refMat.Rows - tplMat.Rows + 1, refMat.Cols - tplMat.Cols + 1, MatType.CV_32FC1))
            {
                //Convert input images to gray
                Mat gref = refMat.CvtColor(ColorConversionCodes.BGR2GRAY);
                Mat gtpl = tplMat.CvtColor(ColorConversionCodes.BGR2GRAY);

                OpenCvSharp.Point minLoc, maxLoc;

                Cv2.MatchTemplate(gref, gtpl, res, TemplateMatchModes.CCoeffNormed);
                Cv2.Threshold(res, res, 0.93, 1.0, ThresholdTypes.Binary);

                while (true)
                {
                    double minVal, maxVal, threshold = 0.93;
                    Cv2.MinMaxLoc(res, out minVal, out maxVal, out minLoc, out maxLoc);

                    if (maxVal >= threshold)
                    {
                        Console.WriteLine("Found Image Match in location: {0} {1}", minLoc, maxLoc);
                        if (found.Where(x => x.X + 20 >= maxLoc.X && x.X - 20 <= maxLoc.X && x.Y + 20 >= maxLoc.Y && x.Y - 20 <= maxLoc.Y).FirstOrDefault() != null)
                            found.Add(maxLoc);
                        

                        // drawing function
                        DrawMatch(maxLoc, tplMat, refMat, res);
                    }
                    else
                        break;
                }
                return found;
            }
        }

        private static void DrawMatch(OpenCvSharp.Point maxLoc, Mat tplMat, Mat refMat, Mat res)
        {
            //Setup the rectangle to draw
            Rect r = new Rect(new OpenCvSharp.Point(maxLoc.X, maxLoc.Y), new OpenCvSharp.Size(tplMat.Width, tplMat.Height));

            //Draw a rectangle of the matching area
            Cv2.Rectangle(refMat, r, Scalar.LimeGreen, 2);

            //Fill in the res Mat so you don't find the same area again in the MinMaxLoc
            Rect outRect;
            Cv2.FloodFill(res, maxLoc, new Scalar(0), out outRect, new Scalar(0.1), new Scalar(1.0));
        }
    }
}
