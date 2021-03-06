﻿using AllEnum;
using GMap.NET;
using GMap.NET.MapProviders;
using GMap.NET.WindowsForms;
using GMap.NET.WindowsForms.Markers;
using PokemonGo.RocketAPI.Enums;
using PokemonGo.RocketAPI.Exceptions;
using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Data;
using System.Drawing;
using System.Globalization;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Forms;

namespace PokemonGo.RocketAPI.GUI
{
    public partial class Form1 : Form
    {
        public static Form1 gameForm;

        GMapOverlay pokeStopsOverlay;
        GMapOverlay playerOverlay;
        GMapOverlay pokemonsOverlay;

        TimeSpan ts = new TimeSpan();

        Logic.Logic mainLogicThread;

        private int expGained = 0;
        private int starDust = 0;
        private int pokestopsFarmed = 0;
        private int pokemonsFarmed = 0;

        bool loadingSettings = false;
        bool formBeingClosed = false;
        bool gotNewCoord = false;
        bool moveRequired = false;
        private double _lat, _lng;
        private double _latStep = 0, _lngStep = 0;

        Settings startSettings;

        GMarkerGoogle playerMarker;
        GMarkerGoogle forceMoveMarker;

        Task mainTask;
        CancellationTokenSource mainTaskCancel;

        public double Lat = UserSettings.Default.DefaultLatitude, Lng = UserSettings.Default.DefaultLongitude;

        Dictionary<string, GMapMarker> mapMarkers = new Dictionary<string, GMapMarker>();

        private void Form1_Load(object sender, EventArgs e)
        {
            gameForm = this;

            loadingSettings = true;
            startSettings = new Settings();

            comboBox1.DataSource = Enum.GetValues(typeof(AuthType));

            comboBox1.SelectedItem = startSettings.AuthType;

            updateLatLngTextBox();

            tbLogin.Text = startSettings.PtcUsername;
            tbPswd.Text = startSettings.PtcPassword;

            chbUseProxy.Checked = startSettings.UseProxy;
            SwitchProxyBox(!startSettings.UseProxy);
            tbProxyUri.Text = startSettings.ProxyUri;
            tbProxyLogin.Text = startSettings.ProxyLogin;
            tbProxyPass.Text = startSettings.ProxyPass;

            chbAutoEvolve.Checked = startSettings.AutoEvolve;
            chbAutoTransfer.Checked = startSettings.AutoTransfer;
            chbTransferWeak.Checked = startSettings.TransferOnlyWeak;

            trackBar1.Value = UserSettings.Default.MoveSpeedFactor;

            loadingSettings = false;

            gMapControl1.Bearing = 0;

            gMapControl1.CanDragMap = true;

            gMapControl1.DragButton = MouseButtons.Left;

            gMapControl1.GrayScaleMode = true;

            gMapControl1.MarkersEnabled = true;

            gMapControl1.MaxZoom = 18;

            gMapControl1.MinZoom = 2;

            gMapControl1.MouseWheelZoomType = MouseWheelZoomType.MousePositionWithoutCenter;

            gMapControl1.NegativeMode = false;

            gMapControl1.PolygonsEnabled = true;

            gMapControl1.ShowCenter = false;

            gMapControl1.RoutesEnabled = true;

            gMapControl1.ShowTileGridLines = false;

            gMapControl1.Zoom = 18;

            gMapControl1.MapProvider = GMap.NET.MapProviders.GMapProviders.GoogleMap;
            GMap.NET.GMaps.Instance.Mode = GMap.NET.AccessMode.ServerOnly;

            gMapControl1.Position = new GMap.NET.PointLatLng(Lat, Lng);

            pokeStopsOverlay = new GMapOverlay("pokeStops");
            gMapControl1.Overlays.Add(pokeStopsOverlay);
            playerOverlay = new GMapOverlay("playerMarker");
            gMapControl1.Overlays.Add(playerOverlay);
            pokemonsOverlay = new GMapOverlay("pokemons");
            gMapControl1.Overlays.Add(pokemonsOverlay);

            GMap.NET.MapProviders.GMapProvider.WebProxy = System.Net.WebRequest.GetSystemWebProxy();
            GMap.NET.MapProviders.GMapProvider.WebProxy.Credentials = System.Net.CredentialCache.DefaultCredentials;

            SetMapObjects();
            MovePlayer();
        }

        private void updateLatLngTextBox()
        {
            tbLat.Text = startSettings.DefaultLatitude.ToString();
            tbLng.Text = startSettings.DefaultLongitude.ToString();
        }

        public void PushNewRow(string rowText, Color rowColor)
        {
            Invoke(new Action(() =>
            {
                gameForm?.richTextBox1.AppendText(rowText, rowColor);
            }));
        }

        public void PushCounterInfo(string infoType, int amout)
        {
            switch (infoType)
            {
                case "ps":
                    pokestopsFarmed += amout;
                    Invoke(new Action(() =>
                    {
                        tbPokestops.Text = pokestopsFarmed.ToString();
                    }));
                    break;
                case "pm":
                    pokemonsFarmed += amout;
                    Invoke(new Action(() =>
                    {
                        tbPokemons.Text = pokemonsFarmed.ToString();
                    }));
                    break;
                default:
                    break;
            }
        }


        public void PushNewInfo(string infoType, string info)
        {
            int gained = 0;
            double coord = 0;            

            switch (infoType)
            {
                case "profileInfo":
                    Invoke(new Action(() =>
                    {
                        this.Text = $"PokemonGo Bot GUI -> {info}";
                    }));
                    break;
                case "xpGained":
                    int.TryParse(info, out gained);
                    expGained += gained;
                    Invoke(new Action(() =>
                    {
                        xpBox.Text = expGained.ToString();
                    }));
                    break;
                case "sdGained":
                    int.TryParse(info, out gained);
                    starDust += int.Parse(info);
                    Invoke(new Action(() =>
                    {
                        sdBox.Text = starDust.ToString();
                    }));
                    break;
                case "wipe":
                    starDust = 0;
                    Invoke(new Action(() =>
                    {
                        sdBox.Text = "0";
                    }));
                    break;
                case "nextLat":
                    double.TryParse(info, out coord);
                    Lat = coord;
                    break;
                case "nextLng":
                    double.TryParse(info, out coord);
                    Lng = coord;
                    Invoke(new Action(() =>
                    {
                        UpdateCoords();
                    }));
                    break;
                case "fm_rm":
                    if (forceMoveMarker != null)
                    {
                        playerOverlay.Markers.Remove(forceMoveMarker);
                        forceMoveMarker = null;
                    }
                    break;
            }
        }

        Queue<NewMapObject> qMap = new Queue<NewMapObject>();


        public void PushNewMapObject(string oType, string oName, double lat, double lng, string uid)
        {
            NewMapObject nmo = new NewMapObject(oType, oName, lat, lng, uid);
            qMap.Enqueue(nmo);            
        }

        private async void SetMapObjects()
        {
            while (!formBeingClosed)
            {
                if (qMap.Count > 0)
                {
                    var newMapObj = qMap.Dequeue();
                    switch (newMapObj.oType)
                    {
                        case "ps":
                            if (!mapMarkers.ContainsKey(newMapObj.uid))
                            {
                                GMarkerGoogle marker = new GMarkerGoogle(new PointLatLng(newMapObj.lat, newMapObj.lng), newMapObj.oName != "lured" ? Properties.Resources.Pstop : Properties.Resources.pstop_lured);
                                marker.ToolTipText = "PokeStop";
                                pokeStopsOverlay.Markers.Add(marker);
                                mapMarkers.Add(newMapObj.uid, marker);
                            }
                            break;
                        case "pm_rm":
                            if (mapMarkers.ContainsKey(newMapObj.uid))
                            {
                                pokemonsOverlay.Markers.Remove(mapMarkers[newMapObj.uid]);
                            }
                            break;
                        case "pm":
                            if (!mapMarkers.ContainsKey(newMapObj.uid))
                            {
                                CreatePokemonMarker(newMapObj.oName, newMapObj.lat, newMapObj.lng, newMapObj.uid);
                            }
                            break;
                        default:
                            break;
                    }

                }
                await Task.Delay(10);
            }
        }

        private async void MovePlayer()
        {
            int delay = 25;
            while (!formBeingClosed)
            {
                if (moveRequired)
                {
                    if (gotNewCoord)
                    {
                        _latStep = (Lat - _lat) / (2000 / delay);
                        _lngStep = (Lng - _lng) / (2000 / delay);
                        gotNewCoord = false;
                    }

                    _lat += _latStep;
                    _lng += _lngStep;
                    playerMarker.Position = new PointLatLng(_lat, _lng);
                    if (Math.Abs(_lat - Lat) < 0.000000001 && Math.Abs(_lng - Lng) < 0.000000001)
                        moveRequired = false;                       
                }
                await Task.Delay(delay);
            }
        }

        public void CreatePokemonMarker(string oName, double lat, double lng, string uid)
        {
            PokemonId pokemon = (PokemonId)Enum.Parse(typeof(PokemonId), oName);

            GMarkerGoogle marker = new GMarkerGoogle(new PointLatLng(lat, lng), pokemon.ToBitmap());
            marker.ToolTipText = oName;
            pokemonsOverlay.Markers.Add(marker);
            mapMarkers.Add(uid, marker);
        }


        public Form1()
        {
            InitializeComponent();           

        }

        private void richTextBox1_TextChanged(object sender, EventArgs e)
        {
            SuspendLayout();
            var rtb = sender as RichTextBox;
            Point pt = rtb.GetPositionFromCharIndex(rtb.SelectionStart);
            if (pt.Y > rtb.Height)
            {
                rtb.ScrollToCaret();
            }
            ResumeLayout(true);
        }

        private void coordsLinkLb_Click(object sender, EventArgs e)
        {
            NumberFormatInfo nfi = new NumberFormatInfo();
            nfi.NumberDecimalSeparator = ".";
            System.Diagnostics.Process.Start($"https://www.google.ru/maps?z=18&q=loc:{Lat.ToString(nfi)},{Lng.ToString(nfi)}");
        }

        void UpdateCoords()
        {
            NumberFormatInfo nfi = new NumberFormatInfo();
            nfi.NumberDecimalSeparator = ".";
            coordsLinkLb.Text = $"{Lat.ToString(nfi)}, {Lng.ToString(nfi)}";

            moveRequired = true;

            if (playerMarker == null)
            {
                playerMarker = new GMarkerGoogle(new PointLatLng(Lat, Lng), Properties.Resources.trainer.ResizeImage(30, 50));
                _lat = Lat;
                _lng = Lng;
                playerOverlay.Markers.Add(playerMarker);
            }
            else
            {
                gotNewCoord = true;                
                UserSettings.Default.DefaultLatitude = Lat;
                UserSettings.Default.DefaultLongitude = Lng;
            }            
        }

        private void tbLogin_TextChanged(object sender, EventArgs e)
        {
            UserSettings.Default.PtcUsername = (sender as TextBox).Text;           
        }

        private void Form1_FormClosing(object sender, FormClosingEventArgs e)
        {
            formBeingClosed = true;
            UserSettings.Default.Save();

            mainTaskCancel?.Cancel();
        }

        private void comboBox1_SelectedIndexChanged(object sender, EventArgs e)
        {
            var cb = (sender as ComboBox);
            if (cb.SelectedIndex > -1 && !loadingSettings)
            {
                AuthType auth;
                Enum.TryParse((sender as ComboBox).SelectedValue.ToString(), out auth);
                UserSettings.Default.AuthType = auth.ToString();
            }
        }

        private void tbPswd_TextChanged(object sender, EventArgs e)
        {
            UserSettings.Default.PtcPassword = (sender as TextBox).Text;
        }

        private void tbLat_TextChanged(object sender, EventArgs e)
        {
            double newVal = 0;
            if (double.TryParse((sender as TextBox).Text, out newVal))
                UserSettings.Default.DefaultLatitude = newVal;
        }

        private void tbLng_TextChanged(object sender, EventArgs e)
        {
            double newVal = 0;
            if (double.TryParse((sender as TextBox).Text, out newVal))
                UserSettings.Default.DefaultLongitude = newVal;
        }

        

        private void button1_Click(object sender, EventArgs e)
        {
            (sender as Button).Enabled = false;
            (sender as Button).BackColor = Color.LightGreen;
            groupBox1.Enabled = groupBox2.Enabled = groupBox4.Enabled = false;
            Logger.SetLogger(new FormLogger(LogLevel.Info));
            timer1.Start();
            mainTaskCancel = new CancellationTokenSource();
            var ct = mainTaskCancel.Token;
            mainTask = Task.Run(() =>
            {
                try
                {
                    mainLogicThread = new Logic.Logic(new Settings());
                    mainLogicThread.Execute();
                }
                catch (PtcOfflineException)
                {
                    Logger.Write("PTC Servers are probably down OR your credentials are wrong. Try google", LogLevel.Error);
                }
                catch (Exception ex)
                {
                    Logger.Write($"Unhandled exception: {ex}", LogLevel.Error);
                }
            }, ct);
        }

        private void gMapControl1_OnMarkerClick(GMapMarker item, MouseEventArgs e)
        {
            var markerId = mapMarkers.Where(x => x.Value == item).FirstOrDefault().Key;
            if (markerId != null)
                mainLogicThread.ForceMoveToPokestop(markerId);
        }

        private void checkBox4_CheckedChanged(object sender, EventArgs e)
        {
            UserSettings.Default.UseProxy = (sender as CheckBox).Checked;

            SwitchProxyBox(!UserSettings.Default.UseProxy);
        }

        void SwitchProxyBox(bool sw)
        {
            tbProxyUri.ReadOnly = tbProxyLogin.ReadOnly = tbProxyPass.ReadOnly = sw;
        }

        private void checkBox2_CheckedChanged(object sender, EventArgs e)
        {
            UserSettings.Default.AutoTransfer = (sender as CheckBox).Checked;
            chbTransferWeak.Enabled = UserSettings.Default.AutoTransfer;
        }

        private void checkBox3_CheckedChanged(object sender, EventArgs e)
        {
            UserSettings.Default.TransferOnlyWeak = (sender as CheckBox).Checked;
        }

        private void checkBox1_CheckedChanged(object sender, EventArgs e)
        {
            UserSettings.Default.AutoEvolve = (sender as CheckBox).Checked;
        }

        private void textBox3_TextChanged(object sender, EventArgs e)
        {
            UserSettings.Default.ProxyUri = (sender as TextBox).Text;
        }

        private void textBox4_TextChanged(object sender, EventArgs e)
        {
            UserSettings.Default.ProxyLogin = (sender as TextBox).Text;
        }

        private void textBox5_TextChanged(object sender, EventArgs e)
        {
            UserSettings.Default.ProxyPass = (sender as TextBox).Text;
        }

        private void richTextBox1_LinkClicked(object sender, LinkClickedEventArgs e)
        {
            System.Diagnostics.Process.Start(e.LinkText);
        }

        private void trackBar1_Scroll(object sender, EventArgs e)
        {
            UserSettings.Default.MoveSpeedFactor = (sender as TrackBar).Value;
        }

        private void gMapControl1_MouseDoubleClick(object sender, MouseEventArgs e)
        {
            //New point coord
            double lat = 0.0;
            double lng = 0.0;

            //Getting real coordinates from mouse click
            lat = gMapControl1.FromLocalToLatLng(e.X, e.Y).Lat;
            lng = gMapControl1.FromLocalToLatLng(e.X, e.Y).Lng;

            if (mainLogicThread != null)
            {
                if (forceMoveMarker == null)
                {
                    forceMoveMarker = new GMarkerGoogle(new PointLatLng(lat, lng), Properties.Resources.force_move);
                    playerOverlay.Markers.Add(forceMoveMarker);
                }
                else
                {
                    forceMoveMarker.Position = new PointLatLng(lat, lng);
                }

                mainLogicThread.ForceMoveToLocation(lat, lng);
            }
            else
            {
                UserSettings.Default.DefaultLatitude = lat;
                UserSettings.Default.DefaultLongitude = lng;
                if (playerMarker == null)
                {
                    playerMarker = new GMarkerGoogle(new PointLatLng(lat, lng), Properties.Resources.trainer.ResizeImage(30, 50));
                    playerOverlay.Markers.Add(playerMarker);
                }
                else
                {
                    playerMarker.Position = new PointLatLng(lat, lng);
                }
                updateLatLngTextBox();
            }
        }

        private void timer1_Tick(object sender, EventArgs e)
        {
            ts = ts.Add(new TimeSpan(0, 0, 1));
            timerLb.Text = ts.ToString();
        }
    }

    internal class NewMapObject
    {
        public string oType;
        public string oName;
        public double lat;
        public double lng;
        public string uid;
        public NewMapObject(string _oType, string _oName, double _lat, double _lng, string _uid)
        {
            oType = _oType;
            oName = _oName;
            lat = _lat;
            lng = _lng;
            uid = _uid;
        }
    }
}
