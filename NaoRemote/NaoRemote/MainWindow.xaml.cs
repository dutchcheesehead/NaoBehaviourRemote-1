﻿using System;
using System.Linq;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Threading;

using Aldebaran.Proxies;
using System.ComponentModel;

namespace NaoRemote
{
    
    /// <summary>
    /// Interaction logic for MainWindow.xaml
    /// </summary>
    public partial class MainWindow : Window
    {
        //delegators for asynchronous calls
        private delegate void NoArgDelegate();
        private delegate void UpdateInterfaceAfterConnectDelegate(bool arg);
        private delegate void BehaviorSequenceDelegate(BehaviorSequence arg);
        private delegate void BehaviorWaiterDelegate(int ID);
        private delegate void ConnectToNaoDelegate(string ip_address, int port);
 
        //naoqi proxies
        private TextToSpeechProxy TextToSpeechProxy;
        private BehaviorManagerProxy BehaviorManagerProxy;
        private LedsProxy LedsProxy;
        private VideoRecorderProxy VideoRecorderProxy;

        //behavior and trial sequences
        private BehaviorSequence currentSequence = BehaviorSequence.EmptyBehaviorSequence();
        private TrialSequence TrialSequence; 

        private int SubjectNumber;

        public MainWindow()
        {
            InitializeComponent();
            TrialSequence = TrialSequence.CreateEmptyTrialSequence();
            UpdateSequenceButtonContext();
            SetWozButtonsEnabled(false);
        }

        private void WaitForBehaviorToFinish(int ID)
        {
            bool continueWaiting = BehaviorManagerProxy.isRunning(ID);
            if(continueWaiting)
            {
                CurrentlyRunningLabel.Dispatcher.BeginInvoke(DispatcherPriority.SystemIdle,
                    new BehaviorWaiterDelegate(WaitForBehaviorToFinish), ID);
            }
            else
            {
                BehaviorFinished();
            }
        }

        private void BehaviorButtonHandler(object sender, RoutedEventArgs e)
        {
            string behaviorName = TextBoxNaoBehaviorRoot.Text + (string)((Button)sender).Tag;
            if (BehaviorManagerProxy.isBehaviorPresent(behaviorName))
            {
                RunBehavior(behaviorName);
            }
            else
            {
                MessageBox.Show("The behavior \"" + behaviorName + "\" was not located on Nao.",
                    "Unknown Behavior");
            }
        }

        private void StopButtonHandler(object sender, RoutedEventArgs e)
        {
            CurrentlyRunningLabel.Content = "Stopping all behaviors...";
            StopAllBehaviors();
        }

        private void BehaviorSequenceHandler(object sender, RoutedEventArgs e)
        {
            if(TrialSequence.Count > 0) {
                currentSequence = TrialSequence.Last();
                TrialSequence.RemoveAt(TrialSequence.Count -1);
                SequenceButton.Content = "Next Trial (" + TrialSequence.Count + ")";
                SequenceButton.IsEnabled = false;
                RunBehaviorSequence();
            }
            else
                MessageBox.Show("All Trials Completed!!.",
                    "Trials Completed");
        }

        private void RunBehavior(string behaviorName)
        {
            CurrentlyRunningLabel.Content = "Currently Running: " + behaviorName;
            int ID = BehaviorManagerProxy.post.runBehavior(behaviorName);
            LogBehavior(behaviorName);
            CurrentlyRunningLabel.Dispatcher.BeginInvoke(DispatcherPriority.Normal, 
                new BehaviorWaiterDelegate(WaitForBehaviorToFinish), ID);
        }

        private void RunBehaviorSequence()
        {
            if (!VideoRecorderProxy.isRecording())
                    StartVideoRecording();
            string behaviorToRun = currentSequence.First();
            currentSequence.Remove(behaviorToRun);
            RunBehavior(behaviorToRun);
        }

        private void BehaviorFinished()
        {
            if (currentSequence.Count > 0)
                RunBehaviorSequence();
            else
                UpdateUserInterfaceAfterBehaviorRun();
        }

        private void UpdateSequenceButtonContext()
        {
            SequenceButton.Content = "Next Trial (" + TrialSequence.Count + ")";
        }

        private void UpdateUserInterfaceAfterBehaviorRun()
        {
            CurrentlyRunningLabel.Content = "Currently Running: None";
            if (currentSequence.Count == 0)
                SequenceButton.IsEnabled = true;
        }

        private void StopAllBehaviors()
        {
            try
            {
                int ID = BehaviorManagerProxy.post.stopAllBehaviors();
                UpdateUserInterfaceAfterBehaviorRun();
            }
            finally { }
        }

        private void NetworkSettingsUpdated(object sender, RoutedEventArgs e)
        {
            try
            {
                string nao_ip_address = TextBoxNaoIP.Text;
                int nao_port = Int32.Parse(TextBoxNaoPort.Text);
                ConnectButton.IsEnabled = false;
                ConnectButton.Content = "Connecting...";

                ConnectToNaoDelegate connector = new ConnectToNaoDelegate(this.ConnectToNao);
                connector.BeginInvoke(nao_ip_address, nao_port, null, null);
            }
            catch (FormatException)
            {
                //the field colors red magically
            }
        }

        private void StartVideoRecording()
        {
            VideoRecorderProxy.setFrameRate(Properties.Settings.Default.FrameRate);
            VideoRecorderProxy.setResolution(Properties.Settings.Default.VideoResolution);
            VideoRecorderProxy.setVideoFormat(Properties.Settings.Default.VideoFormat);
            VideoRecorderProxy.startRecording(Properties.Settings.Default.VideoDirectory,CreateVideoFileName());
        }

        private void WriteLogFileHeader(StreamWriter writer)
        {
            string sep = Properties.Settings.Default.CSVFieldSeparator;
            writer.Write("SubjectNumber");
            writer.Write(sep);
            writer.Write("Date");
            writer.Write(sep);
            writer.Write("Time");
            writer.Write(sep);
            writer.Write("Sequence");
            writer.Write(sep);
            writer.Write("TrialNumber");
            writer.Write(sep);
            writer.Write("TrialCode");
            writer.Write(sep);
            writer.Write("BehaviorName");
            writer.WriteLine("");
        }
        
        private void WriteLogFileData(StreamWriter writer, string behaviorName)
        {
            string sep = Properties.Settings.Default.CSVFieldSeparator;
            string date = System.DateTime.Now.ToString("dd-MM-yyyy");
            string time = System.DateTime.Now.ToString("HH:mm:ss");
            writer.Write(this.SubjectNumber);
            writer.Write(sep);
            writer.Write(date);
            writer.Write(sep);
            writer.Write(time);
            writer.Write(sep);
            writer.Write(TrialSequence.GetName());
            writer.Write(sep);
            writer.Write(TrialSequence.TrialNumber());
            writer.Write(sep);
            writer.Write(currentSequence.GetName());
            writer.Write(sep);
            writer.Write(behaviorName);
            writer.WriteLine("");

        }

        private void LogBehavior(string behaviorName)
        {
            string logFilePath = Path.Combine(Directory.GetCurrentDirectory(), Properties.Settings.Default.LogFileName);
            bool writeHeader = !File.Exists(logFilePath);
            using (FileStream fs = new FileStream(logFilePath, FileMode.Append))
            {
                using (StreamWriter LogWriter = new StreamWriter(fs))
                {
                    try
                    {
                        if (writeHeader)
                            this.WriteLogFileHeader(LogWriter);
                        this.WriteLogFileData(LogWriter, behaviorName);
                    }
                    catch (IOException ioe)
                    {

                    }
                }
            }
        }



        private string CreateVideoFileName()
        {
            return Properties.Settings.Default.VideoFilePrefix + SubjectNumber + Properties.Settings.Default.VideoFileSuffix;
        }

        private void DisposeOfAllProxies()
        {
            if (TextToSpeechProxy != null)
                TextToSpeechProxy.Dispose();
            if (BehaviorManagerProxy != null)
                BehaviorManagerProxy.Dispose();
            if (LedsProxy != null)
                LedsProxy.Dispose();
            if (VideoRecorderProxy != null)
            {
                if (VideoRecorderProxy.isRecording())
                    VideoRecorderProxy.stopRecording();
                VideoRecorderProxy.Dispose();
            }
        }

        private void ConnectToNao(string nao_ip_address, int nao_port)
        {
            bool success = true;
            try
            {
                TextToSpeechProxy = new TextToSpeechProxy(nao_ip_address, nao_port);
                BehaviorManagerProxy = new BehaviorManagerProxy(nao_ip_address, nao_port);
                LedsProxy = new LedsProxy(nao_ip_address, nao_port);
                VideoRecorderProxy = new VideoRecorderProxy(nao_ip_address, nao_port);
            }
            catch (Exception)
            {
                success = false;
            }
            ConnectButton.Dispatcher.BeginInvoke(DispatcherPriority.Normal,
                new UpdateInterfaceAfterConnectDelegate(UpdateUserInterfaceAfterConnect), success);
        }

        private void UpdateUserInterfaceAfterConnect(bool success)
        {
            if (success)
                MessageBox.Show("You are now connected to Nao with IP-address: " + TextBoxNaoIP.Text +
                    " on port " + TextBoxNaoPort.Text + ".", "Successfully Connected");
            else
                MessageBox.Show("Could not connect to Nao  with IP-address: " + TextBoxNaoIP.Text +
                    " on port " + TextBoxNaoPort.Text + ".", "CONNECTION ERROR");
            ConnectButton.IsEnabled = true;
            ConnectButton.Content = "Connect";
            SetWozButtonsEnabled(true);
        }

        private void SetWozButtonsEnabled(bool enabled)
        {
            BehaviorButton1.IsEnabled = enabled;
            BehaviorButton2.IsEnabled = enabled;
            BehaviorButton3.IsEnabled = enabled;
            BehaviorButton4.IsEnabled = enabled;
            BehaviorButton5.IsEnabled = enabled;
            BehaviorButton6.IsEnabled = enabled;
            BehaviorButton7.IsEnabled = enabled;
            BehaviorButton8.IsEnabled = enabled;
            SequenceButton.IsEnabled = enabled;
            StopAllBehaviorsButton.IsEnabled = enabled;
            SayButton.IsEnabled = enabled;
        }

        private void InterfaceWindowClosing(object sender, CancelEventArgs e)
        {
            DisposeOfAllProxies();
            Properties.Settings.Default.Save();
        }

        private void SayWords(object sender, RoutedEventArgs e)
        {
            this.TextToSpeechProxy.post.say(words_to_say.Text);
        }

        internal void SetSubjectNumber(int SubjectNumber)
        {
            this.SubjectNumber = SubjectNumber;
            if(SubjectNumber % 2 == 0) 
            {
                this.TrialSequence = TrialSequence.CreatePredictiveTrialSequence();
            }
            else 
            {
                this.TrialSequence = TrialSequence.CreateUnpredictiveTrialSequence();
            }
            UpdateSequenceButtonContext();
        }
    }
}
