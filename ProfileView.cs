using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Shapes;

namespace AuroraPAR
{
    internal class ProfileView(Canvas canvas, Runway runway)
    {
        private double xscale = 1;
        private double yscale = 1;
        private Canvas Canvas = canvas;
        public Runway Runway = runway;
        private void CalculateScale()
        {
            xscale = (Canvas.ActualWidth - 50) / (Runway.Distance+Runway.LengthNM);
            yscale = (Canvas.ActualHeight - 50) / (((Runway.Distance+Runway.LengthNM) * Math.Tan((Runway.GlideSlope + 5) * double.Pi / 180)* 6076.11549));
        }
        private void DrawAircraft(string callsign, double distance, double altitude, double distanceFromTouchdown)
        {
            var color = Brushes.White;
            double calculatedUp = distance * Math.Tan((Runway.GlideSlope + 0.5) * double.Pi / 180) * 6076.11549 + Runway.TCH + Runway.Elevation;
            double calculatedDown = distance * Math.Tan((Runway.GlideSlope - 0.5) * double.Pi / 180) * 6076.11549 + Runway.TCH + Runway.Elevation;
            if (altitude < calculatedUp && altitude > calculatedDown)
            {
                color = Brushes.Green;
            }
            else
            {
                color = Brushes.Red;
            }
            Ellipse elipse = new()
            {
                Height = 10,
                Width = 10,
                Stroke = color,
                Fill = color
            };
            Canvas.SetBottom(elipse, (altitude - Runway.Elevation) * yscale - 5);
            Canvas.SetLeft(elipse, (distance + Runway.LengthNM) * xscale - 5);
            Canvas.Children.Add(elipse);
            TextBlock textBlock = new()
            {
                Text = $"{callsign}\n{altitude}\n{distanceFromTouchdown:0.0}NM",
                FontSize = 12,
                Foreground = Brushes.White
            };
            Canvas.SetBottom(textBlock, ((altitude - Runway.Elevation) * yscale + 15));
            Canvas.SetLeft(textBlock, ((distance + Runway.LengthNM) * xscale) - 15);
            Canvas.Children.Add(textBlock);
        }
        public void Draw(List<Aircraft> aircrafts)
        {
            CalculateScale();
            Line horizontal = new()
            {
                X1 = Runway.LengthNM*xscale,
                Y1 = Canvas.ActualHeight,
                X2 = (Runway.Distance+Runway.LengthNM) * xscale,
                Y2 = Canvas.ActualHeight,
                Stroke = Brushes.Green,
                StrokeThickness = 2
            };
            Canvas.Children.Add(horizontal);
            Line runwayh = new()
            {
                X1 = 0,
                Y1 = Canvas.ActualHeight,
                X2 = Runway.LengthNM * xscale,
                Y2 = Canvas.ActualHeight,
                Stroke = Brushes.Green,
                StrokeThickness = 3
            };
            Canvas.Children.Add(runwayh);
            Line runwayv = new()
            {
                X1 = Runway.LengthNM * xscale,
                Y1 = Canvas.ActualHeight,
                X2 = Runway.LengthNM * xscale,
                Y2 = Canvas.ActualHeight - ((Runway.LengthNM * Math.Tan((Runway.GlideSlope + 5) * double.Pi / 180) * 6076.11549) * yscale),
                Stroke = Brushes.Green,
                StrokeThickness = 3
            };
            Canvas.Children.Add(runwayv);
            Line upper = new()
            {
                X1 = 0,
                Y1 = Canvas.ActualHeight,
                X2 = (Runway.LengthNM+Runway.Distance) * xscale,
                Y2 = Canvas.ActualHeight - (((Runway.LengthNM+Runway.Distance) * Math.Tan((Runway.GlideSlope + 5) * double.Pi / 180) * 6076.11549) * yscale),
                Stroke = Brushes.CadetBlue,
                StrokeThickness = 3
            };
            Canvas.Children.Add(upper);
            Line glidepath = new()
            {
                X1 = Runway.LengthNM * xscale,
                Y1 = Canvas.ActualHeight - (Runway.TCH * yscale),
                X2 = (Runway.Distance + Runway.LengthNM) * xscale,
                Y2 = Canvas.ActualHeight-((Runway.Distance * Math.Tan(Runway.GlideSlope * double.Pi / 180) * 6076.11549 + Runway.TCH)*yscale),
                Stroke = Brushes.Yellow,
                StrokeThickness = 2
            };
            Canvas.Children.Add(glidepath);
            Line glidepathM05 = new()
            {
                X1 = Runway.LengthNM * xscale,
                Y1 = Canvas.ActualHeight - (Runway.TCH * yscale),
                X2 = (Runway.Distance + Runway.LengthNM) * xscale,
                Y2 = Canvas.ActualHeight - ((Runway.Distance * Math.Tan((Runway.GlideSlope-0.5) * double.Pi / 180) * 6076.11549 + Runway.TCH) * yscale),
                Stroke = Brushes.Red,
                StrokeThickness = 1
            };
            Canvas.Children.Add(glidepathM05);
            Line glidepathP05 = new()
            {
                X1 = Runway.LengthNM * xscale,
                Y1 = Canvas.ActualHeight - (Runway.TCH * yscale),
                X2 = (Runway.Distance + Runway.LengthNM) * xscale,
                Y2 = Canvas.ActualHeight - ((Runway.Distance * Math.Tan((Runway.GlideSlope + 0.5) * double.Pi / 180) * 6076.11549 + Runway.TCH) * yscale),
                Stroke = Brushes.Red,
                StrokeThickness = 1
            };
            Canvas.Children.Add(glidepathP05);
            Line MDH = new()
            {
                X1 = 0,
                Y1 = Canvas.ActualHeight - (Runway.MDH * yscale),
                X2 = (Runway.LengthNM + ((Runway.MDH - Runway.TCH) / 6076.11549) / Math.Tan(Runway.GlideSlope * double.Pi / 180)) * xscale,
                Y2 = Canvas.ActualHeight - (Runway.MDH * yscale),
                Stroke = Brushes.Red,
                StrokeThickness = 2
            };
            Canvas.Children.Add(MDH);
            Line MAPt = new()
            {
                X1 = (Runway.LengthNM + ((Runway.MDH - Runway.TCH) / 6076.11549) / Math.Tan(Runway.GlideSlope * double.Pi / 180)) * xscale,
                Y1 = Canvas.ActualHeight,
                X2 = (Runway.LengthNM + ((Runway.MDH - Runway.TCH) / 6076.11549) / Math.Tan(Runway.GlideSlope * double.Pi / 180)) * xscale,
                Y2 = Canvas.ActualHeight - (Runway.MDH * yscale),
                Stroke = Brushes.Red,
                StrokeThickness = 2
            };
            Canvas.Children.Add(MAPt);
            // Touchdown point: origin of the range marks.
            Line touchdown = new()
            {
                X1 = (Runway.LengthNM - Runway.TouchdownNM) * xscale,
                Y1 = Canvas.ActualHeight,
                X2 = (Runway.LengthNM - Runway.TouchdownNM) * xscale,
                Y2 = Canvas.ActualHeight - 12,
                Stroke = Brushes.Yellow,
                StrokeThickness = 2
            };
            Canvas.Children.Add(touchdown);
            int num = 10;
            if(Runway.Distance == 15)
            {
                num = 15;
            }
            for (int i = 1; i <= num; i++)
            {
                SolidColorBrush stroke;
                if (i % 5 == 0)
                {
                    stroke = Brushes.Orange;  
                } else
                {
                    stroke = Brushes.Green;
                }
                // Range marks are measured from the touchdown point (not from the threshold),
                // as the distances given by the controller on final.
                double markX = Runway.LengthNM - Runway.TouchdownNM + i * Runway.Distance / num;
                Line distance = new()
                {
                    X1 = markX * xscale,
                    Y1 = Canvas.ActualHeight,
                    X2 = markX * xscale,
                    Y2 = Canvas.ActualHeight - ((markX * Math.Tan((Runway.GlideSlope+5) * double.Pi / 180) * 6076.11549) * yscale),
                    Stroke = stroke,
                    StrokeThickness = 1
                };
                Canvas.Children.Add(distance);
                TextBlock textBlock = new()
                {
                    Text = $"{i*Runway.Distance/num}NM",
                    FontSize = 12,
                    Foreground = Brushes.Yellow
                };
                Canvas.SetBottom(textBlock, 0);
                Canvas.SetLeft(textBlock, (markX * xscale-10));
                Canvas.Children.Add(textBlock);
            }
            foreach (Aircraft aircraft in aircrafts)
            {
                if (aircraft.IsDisplayed(runway))
                {
                    DrawAircraft(aircraft.Callsign, aircraft.AlongTrackDistance(runway), aircraft.Altitude, aircraft.DistanceFromTouchdown(runway));
                }
            }
        }
    }
}
