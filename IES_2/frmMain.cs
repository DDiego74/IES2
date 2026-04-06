using IES_2.ECU;
using IES_2.Properties;
using IES_2.Res;
using System;
using System.Collections;
using System.Collections.Generic;
using System.ComponentModel;
using System.Data;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.Runtime.InteropServices;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Forms;
using Windows.Devices.Bluetooth;
using Windows.Devices.Bluetooth.Advertisement;
using ZedGraph;

namespace IES_2
{
    public partial class frmMain : Form
    {
        AboutBox1 aboutDialog;
        private bool[] Request;
        public ecu ECU;
        private Random random;
        private string[,] ecuList;
        private bool DisconnectionPending = false;
        private bool demo = false;
        private bool autodetect = false;
        private bool recording = false;
        private string ecuName;
        private int testIndex, adjIndex;
        private StreamWriter log;
        private StreamWriter exp;
        private Stopwatch time;
        private string logDir;
        private int pasvDelay;
        private volatile byte queryFlag;
        private System.Windows.Forms.Timer tLog, tGraph;
        private RedrawGraphCallback RDC;
        private frmOneByte FrmOneByte;

        // Transport abstraction – allows COM port or Bluetooth LE
        private ISerialTransport activeTransport;
        private SerialPortTransport comTransport;

        // BLE scanning
        private BluetoothLEAdvertisementWatcher bleWatcher;
        private Dictionary<ulong, string> bleDevicesFound;

        [DllImport("winmm.dll", EntryPoint = "timeBeginPeriod", SetLastError = true)]
        private static extern uint TimeBeginPeriod(uint uMilliseconds);

        [DllImport("winmm.dll", EntryPoint = "timeEndPeriod", SetLastError = true)]
        private static extern uint TimeEndPeriod(uint uMilliseconds);

        public void SplashScreen()
        {
            Application.Run(new SplashScreen());
        }

        public frmMain()
        {
            Thread t = new Thread(new ThreadStart(SplashScreen));
            t.Start();
            Thread.Sleep(1000);
            InitializeComponent();
            InitializeBleUI();
            //t.Abort();

            FrmOneByte = new frmOneByte();
            FrmOneByte.Owner = this;

            tLog = new System.Windows.Forms.Timer();
            tGraph = new System.Windows.Forms.Timer();

            tLog.Interval = 2500;
            tLog.Tick += new EventHandler(tLog_Tick);

            tGraph.Interval = 100;
            tGraph.Tick += new EventHandler(tGraph_Tick);

            RDC = new RedrawGraphCallback(RedrawGraph);

            time = new Stopwatch();
            aboutDialog = new AboutBox1();
            infoPanel.Left = this.ClientSize.Width / 2 - infoPanel.Width / 2;
            infoPanel.Top = this.ClientSize.Height / 2 - infoPanel.Height / 2;
            tabControl1.TabPages.Remove(tabPage1);
            tabControl1.TabPages.Remove(tabPage2);
            tabControl1.TabPages.Remove(tabPage3);
            tabControl1.TabPages.Remove(tabPage4);
            tabControl1.TabPages.Remove(tabPage5);
            pasvDelay = 4;
            this.Text += " [v" + System.Reflection.Assembly.GetExecutingAssembly().GetName().Version.Major.ToString() + "." + System.Reflection.Assembly.GetExecutingAssembly().GetName().Version.Minor.ToString() + "]";
            random = new Random();
            Request = new bool[170];
            logDir = Directory.GetCurrentDirectory() + @"\Log";
            if (!Directory.Exists(logDir))
                try
                {
                    Directory.CreateDirectory(logDir);
                }
                catch { }
            SetAccelerator(Keys.F2, new AcceleratorAction(ShowParams));
            SetAccelerator(Keys.F3, new AcceleratorAction(ShowErrors));
            SetAccelerator(Keys.F4, new AcceleratorAction(ShowTests));
            SetAccelerator(Keys.F5, new AcceleratorAction(ShowGraphs));
            SetAccelerator(Keys.F6, new AcceleratorAction(ShowAdjusts));
            SetAccelerator(Keys.A, new AcceleratorAction(CheckAll));
            SetAccelerator(Keys.N, new AcceleratorAction(UncheckAll));
            SetAccelerator(Keys.C, new AcceleratorAction(ClearCodes));
            SetAccelerator(Keys.S, new AcceleratorAction(RecordToggle));
            SetAccelerator(Keys.E, new AcceleratorAction(ExecTest));
            SetAccelerator(Keys.F10, new AcceleratorAction(ConnectToggle));
            SetAccelerator(Keys.F10 | Keys.Control, new AcceleratorAction(ControlConnectToggle));
        }

        #region Accelerators initialsation routines

        readonly Hashtable _accelerators = new Hashtable();

        protected delegate void AcceleratorAction();

        protected void SetAccelerator(Keys keyCombination,
        AcceleratorAction action)
        {
            _accelerators[keyCombination] = action;
        }

        protected override bool ProcessCmdKey(ref Message msg,
        Keys keyData)
        {
            AcceleratorAction action =
            _accelerators[keyData] as AcceleratorAction;
            if (action != null)
            {
                action();
                return true;
            }
            return base.ProcessCmdKey(ref msg, keyData);
        }

        #endregion
        #region Accelerators definitions

        void ShowParams() { tabControl1.SelectedIndex = 0; }
        void ShowErrors() { tabControl1.SelectedIndex = 1; }
        void ShowTests() { tabControl1.SelectedIndex = 2; }
        void ShowGraphs() { tabControl1.SelectedIndex = 3; }
        void ShowAdjusts() { tabControl1.SelectedIndex = 4; }
        void ConnectToggle()
        {
            if (Settings.Default.logEnabled)
                cbQueryErrors.Enabled = true;
            else
                cbQueryErrors.Enabled = false;
            if (recording)
                RecordToggle(); // Stop recording
            if (!ecu.connected)
            {
                demo = false;
                Connect();
            }
            else
                Disconnect();
        }

        void ControlConnectToggle()
        {
            if (Settings.Default.logEnabled)
                cbQueryErrors.Enabled = true;
            else
                cbQueryErrors.Enabled = false;
            if (recording)
                RecordToggle();
            if (!ecu.connected)
            {
                demo = true;
                Connect();
            }
            else
                Disconnect();
        }

        void F11_Disconnect()
        {
            hideUserMessage();
            SetAccelerator(Keys.F11, null);
            _Disconnected();
        }

        void F12_Reconnect()
        {
            hideUserMessage();
            SetAccelerator(Keys.F12, null);
            ECU.InitPasvDiag();
            if (!bgwParameters.IsBusy)
                bgwParameters.RunWorkerAsync();
        }

        #endregion

        private void fillParameters()
        {
            dgvParameters.Rows.Clear();
            DataGridViewRow dgvRow;
            foreach (dataElement row in ECU.engineData)
            {
                dgvRow = dgvParameters.Rows[dgvParameters.Rows.Add()];
                dgvRow.Cells[0].Value = false;
                dgvRow.Cells[1].Value = row.Description;
                dgvRow.Cells[2].Value = "";
                dgvRow.Cells[3].Value = row.Unit;
                dgvRow.Tag = row.RequestSet;
            }
        }

        private void fillErrors()
        {
            dgvErrors.Rows.Clear();
            DataGridViewRow dgvRow;
            foreach (errorElement row in ECU.engineErrors)
            {
                dgvRow = dgvErrors.Rows[dgvErrors.Rows.Add()];
                dgvRow.Cells[0].Value = row.Description;
                dgvRow.Cells[1].Value = "";
                dgvRow.Cells[2].Value = "";
                dgvRow.Visible = false;
            }
            if (ECU.hasIMMO)
                foreach (errorElement row in ECU.immoErrors)
                {
                    dgvRow = dgvErrors.Rows[dgvErrors.Rows.Add()];
                    dgvRow.Cells[0].Value = row.Description;
                    dgvRow.Cells[1].Value = "";
                    dgvRow.Cells[2].Value = "";
                    dgvRow.Visible = false;
                }
        }

        private void fillTests()
        {
            dgvTests.Rows.Clear();
            DataGridViewRow dgvRow;
            foreach (testElement row in ECU.activeTest)
            {
                dgvRow = dgvTests.Rows[dgvTests.Rows.Add()];
                dgvRow.Cells[0].Value = row.Description;
                dgvRow.Tag = row;
            }
        }

        private void fillAdjusts()
        {
            dgvAdjusts.Rows.Clear();
            DataGridViewRow dgvRow;
            foreach (adjustElement row in ECU.adjustments)
            {
                dgvRow = dgvAdjusts.Rows[dgvAdjusts.Rows.Add()];
                dgvRow.Cells[0].Value = row.Description;
                dgvRow.Tag = row;
            }
        }

        private void RefreshEngineErrors()
        {
            for (int i = 0; i < ECU.engineErrors.Length; i++)
            {
                ECU.engineErrors[i].Decode();
                if (ECU.engineErrors[i].isActive | ECU.engineErrors[i].isVerified | ECU.engineErrors[i].isStored)
                {
                    dgvErrors.Rows[i].Visible = true;
                    dgvErrors.Rows[i].Cells[1].Value = ECU.engineErrors[i].Reason;
                    dgvErrors.Rows[i].Cells[2].Value = (ECU.engineErrors[i].isActive ? lang.Act : "") + (ECU.engineErrors[i].isVerified ? lang.Ver : "") + (ECU.engineErrors[i].isStored ? lang.Sto : "");
                    dgvErrors.Rows[i].Cells[3].Value = ECU.engineErrors[i].isActive & (ECU.engineErrors[i].isVerified | ECU.engineErrors[i].isStored);
                }
                else
                    dgvErrors.Rows[i].Visible = false;
            }
        }
        private void RefreshImmoErrors()
        {
            if (ECU.hasIMMO)
                for (int i = 0; i < ECU.immoErrors.Length; i++)
                {
                    ECU.immoErrors[i].Decode();
                    if (ECU.immoErrors[i].isActive | ECU.immoErrors[i].isVerified | ECU.immoErrors[i].isStored)
                    {
                        dgvErrors.Rows[ECU.engineErrors.Length + i].Visible = true;
                        dgvErrors.Rows[ECU.engineErrors.Length + i].Cells[1].Value = ECU.immoErrors[i].Reason;
                        dgvErrors.Rows[ECU.engineErrors.Length + i].Cells[2].Value = (ECU.immoErrors[i].isActive ? lang.Act : "") + (ECU.immoErrors[i].isVerified ? lang.Ver : "") + (ECU.immoErrors[i].isStored ? lang.Sto : "");
                        dgvErrors.Rows[ECU.engineErrors.Length + i].Cells[3].Value = ECU.immoErrors[i].isActive & (ECU.immoErrors[i].isVerified | ECU.immoErrors[i].isStored);
                    }
                    else
                        dgvErrors.Rows[ECU.engineErrors.Length + i].Visible = false;
                }
        }

        private void RefreshData()
        {
            for (int i = 0; i < ECU.engineData.Length; i++)
            {
                if ((bool)dgvParameters.Rows[i].Cells[0].Value == true)
                {
                    dgvParameters.Rows[i].Cells[2].Value = ecu.Valid[ECU.engineData[i].RequestSet[0]] ? ECU.engineData[i].FormattedValue : "#ERR";
                }
            }
        }

        private bool EnsureSelectedRow(DataGridView grid, string action)
        {
            if (grid.SelectedRows.Count > 0)
                return true;
            if (grid.CurrentRow != null)
            {
                grid.CurrentRow.Selected = true;
                return grid.SelectedRows.Count > 0;
            }
            MessageBox.Show(string.Format("Please select a row before {0}.", action), this.Text, MessageBoxButtons.OK, MessageBoxIcon.Information);
            return false;
        }

        private void Connect()
        {
            if (!EnsureSelectedRow(dgvECU, "connecting"))
                return;
            showUserMessage(lang.Connecting, lang.EscCancel);
            ecu.Buffer.Initialize();
            ecu.Valid.Initialize();
            switch ((string)dgvECU.Rows[dgvECU.SelectedRows[0].Index].Tag)
            {
                case iaw16f.name:
                    autodetect = false;
                    ECU = new iaw16f(activeTransport);
                    ecuName = iaw16f.longName;
                    break;
                case iaw18f.name:
                    autodetect = false;
                    ECU = new iaw18f(activeTransport);
                    ecuName = iaw18f.longName;
                    break;
                case iaw8f_68.name:
                    autodetect = false;
                    ECU = new iaw8f_68(activeTransport);
                    ecuName = iaw8f_68.longName;
                    break;
                case iaw18fd.name:
                    autodetect = false;
                    ECU = new iaw18fd(activeTransport);
                    ecuName = iaw18fd.longName;
                    break;
                case iaw04k.name:
                    autodetect = false;
                    ECU = new iaw04k(activeTransport);
                    ecuName = iaw04k.longName;
                    break;
                case code.name:
                    autodetect = false;
                    ECU = new code(activeTransport);
                    ecuName = code.longName;
                    break;
                case "auto":
                    autodetect = true;
                    ECU = null;
                    break;
            }
            if (demo)
            {
                ecu.ISO = "55D085029440";
                ecu.CODRIC = "6160206301";
                if (bgwDemo.IsBusy) return;
                Connected(true);
            }
            else
            {
                if (activeTransport == null)
                {
                    hideUserMessage();
                    MessageBox.Show("Selezionare un dispositivo Bluetooth LE prima di connettersi.", "Bluetooth LE", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                    return;
                }
                try
                {
                    if (!activeTransport.IsOpen)
                        activeTransport.Open();
                }
                catch
                {
                    hideUserMessage();
                    MessageBox.Show(string.Format(lang.COMopenError, activeTransport.PortName), lang.COMnotValid, MessageBoxButtons.OK, MessageBoxIcon.Error);
                    return;
                }
                if (bgwParameters.IsBusy) return;
                if (bgwIsoWait.IsBusy) return;
                SetAccelerator(Keys.Escape, new AcceleratorAction(EscCancelConnect));
                bgwIsoWait.RunWorkerAsync();
            }
        }

        private void Connected(bool IsoReceived)
        {
            TimeBeginPeriod(2);
            ecu.connected = true;
            if (autodetect)
            {
                if (IsoReceived & iaw04k.CheckISO())
                {
                    ECU = new iaw04k(activeTransport);
                    ecuName = iaw04k.longName;
                }
                else
                {
                    ecu.InitPasvDiag(activeTransport);
                    ecu.ReadCODRIC(activeTransport);
                    if (!IsoReceived)
                        ecu.ReadISO(activeTransport);
                    if (iaw18fd.CheckISO())
                    {
                        ECU = new iaw18fd(activeTransport);
                        ecuName = iaw18fd.longName;
                    }
                    else if (iaw18f.CheckISO())
                    {
                        ECU = new iaw18f(activeTransport);
                        ecuName = iaw18f.longName;
                    }
                    else if (iaw8f_68.CheckISO())
                    {
                        ECU = new iaw8f_68(activeTransport);
                        ecuName = iaw8f_68.longName;
                    }
                    else if (iaw16f.CheckISO())
                    {
                        ECU = new iaw16f(activeTransport);
                        ecuName = iaw16f.longName;
                    }
                    else if (iaw04k.CheckISO())
                    {
                        ECU = new iaw04k(activeTransport);
                        ecuName = iaw04k.longName;
                    }
                    else
                    {
                        ecu.connected = false;
                        SetAccelerator(Keys.F11, F11_Disconnect);
                        showUserMessage(lang.UnrecECU, lang.F11_Confirm);
                        return;
                    }
                }
            }
            else
            {
                ECU.InitPasvDiag();
                ECU.ReadCODRIC();
                if (!IsoReceived)
                    ECU.ReadISO();
            }
            ECU.hasIMMO = demo ? true : ecu.CheckCODE(activeTransport);
            lblIsoCode.Text = ecu.ISO != null ? Regex.Replace(ecu.ISO, @"(\w{2})(\w{2})(\w{2})(\w{2})(\w{2})(\w{2})", "$1-$2-$3-$4-$5-$6") : "-";
            lblRepCode.Text = ecu.CODRIC != null ? Regex.Replace(ecu.CODRIC, @"(\w{5})(\w{3})(\w{2})", "$1.$2.$3") : "-";
            lblEcuType.Text = ecuName + (ECU.hasIMMO ? "" : " ECOL");
            lblCarModel.Text = ECU.GetCarModel();
            fillParameters();
            fillErrors();
            fillTests();
            fillAdjusts();
            if (demo)
                bgwDemo.RunWorkerAsync();
            else
                bgwParameters.RunWorkerAsync();
            tabControl1.TabPages.Remove(tabPage0);
            tabControl1.TabPages.Add(tabPage1);
            tabControl1.TabPages.Add(tabPage2);
            tabControl1.TabPages.Add(tabPage3);
            tabControl1.TabPages.Add(tabPage4);
            tabControl1.TabPages.Add(tabPage5);
            btnConnect.Text = lang.F10_Disconnect;
            tLog.Enabled = cbLOG.Checked;
            if (cbLOG.Checked)
            {
                if (Directory.Exists(logDir))
                {
                    log = new StreamWriter(logDir + @"\IESlog_" + DateTime.Now.ToString("yyMMddHHmm") + ".txt", true);
                    log.WriteLine(DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss"));
                    log.WriteLine(this.Text);
                    log.WriteLine(lang.ConnectedTo);
                    log.WriteLine(lblCarModel.Text);
                    log.WriteLine(lblEcuType.Text);
                    if (demo) log.WriteLine(lang.Simulation);
                    log.WriteLine("--------------------------------------------------------------");
                    log.WriteLine();
                    log.WriteLine(label24.Text + " " + lblIsoCode.Text);
                    log.WriteLine(label17.Text + " " + lblRepCode.Text);
                }
                else
                {
                    tLog.Enabled = false;
                    cbLOG.Checked = false;
                    MessageBox.Show("Brak uprawnień do katalogu logów. Logowanie zostało wyłączone.", "Logowanie", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                }
            }
            hideUserMessage();
        }

        private void Disconnect()
        {
            if (ecu.connected & bgwDemo.IsBusy)
            {
                DisconnectionPending = true;
                bgwDemo.CancelAsync();
            }
            else if (ecu.connected & bgwParameters.IsBusy)
            {
                DisconnectionPending = true;
                bgwParameters.CancelAsync();
            }
            else
                Disconnected();
        }

        private void Disconnected()
        {
            SetAccelerator(Keys.F11, new AcceleratorAction(F11_Disconnect));
            showUserMessage(lang.TurnOFF, lang.F11_Confirm);
        }

        private void _Disconnected()
        {
            if (activeTransport != null && activeTransport.IsOpen)
                activeTransport.Close();
            if (ecu.connected)
            {
                lblIsoCode.Text = "-";
                lblRepCode.Text = "-";
                lblEcuType.Text = "-";
                lblCarModel.Text = "-";
                tLog.Enabled = false;
                ecu.connected = false;
                ecu.ISO = null;
                ecu.CODRIC = null;
                cblTraces.Items.Clear();
                DisconnectionPending = false;
                tabControl1.TabPages.Remove(tabPage1);
                tabControl1.TabPages.Remove(tabPage2);
                tabControl1.TabPages.Remove(tabPage3);
                tabControl1.TabPages.Remove(tabPage4);
                tabControl1.TabPages.Remove(tabPage5);
                tabControl1.TabPages.Add(tabPage0);
                btnConnect.Text = lang.F10_Connect;
                TimeEndPeriod(2);
                if (log != null)
                    log.Close();
            }
        }

        private void dgvParameters_CellClick(object sender, DataGridViewCellEventArgs e)
        {
            DataGridView dgv = (DataGridView)sender;
            if (e.RowIndex == -1) return;
            if (e.ColumnIndex == 0)
            {
                if (dgv.Rows[e.RowIndex].Cells[0].Value == null)
                {
                    dgv.Rows[e.RowIndex].Cells[0].Value = true;
                }
                else
                {
                    bool bChecked = (bool)dgv.Rows[e.RowIndex].Cells[0].Value;
                    dgv.Rows[e.RowIndex].Cells[0].Value = !bChecked;
                    foreach (byte idx in (byte[])dgv.Rows[e.RowIndex].Tag)
                    {
                        Request[idx] = !bChecked;
                    }
                    if (!bChecked)
                        cblTraces.Items.Add(dgv.Rows[e.RowIndex].Cells[1].Value, true);
                    else
                        cblTraces.Items.Remove(dgv.Rows[e.RowIndex].Cells[1].Value);
                }
            }
        }

        private void bgwIsoWait_DoWork(object sender, DoWorkEventArgs e)
        {
            BackgroundWorker worker = sender as BackgroundWorker;
            ecu.SetReadTimeout(activeTransport, 1200);
            activeTransport.BaudRate = ecu.initBaud;
            const int wait = 10000; // timeout = 10s
            byte Response;
            bool sync = false;
            int i = 1;
            byte[] ISOcod = new byte[6];
            string ISO;
            Stopwatch sw = new Stopwatch();
            sw.Start();
            while ((sw.ElapsedMilliseconds < wait) & !sync)
            {
                if (worker.CancellationPending)
                {
                    e.Cancel = true;
                    break;
                }
                if (activeTransport.BytesToRead == 0)
                    Thread.Sleep(15);
                else
                {
                    Response = (byte)activeTransport.ReadByte();
                    if (Response == 0x55)
                    {
                        sync = true;
                        ISOcod[0] = Response;
                    }
                }
            }
            sw.Stop();
            if (sync)
            {
                while (i < 6)
                {
                    if (worker.CancellationPending)
                    {
                        e.Cancel = true;
                        break;
                    }
                    Response = (byte)activeTransport.ReadByte();
                    ISOcod[i++] = Response;
                }
            }
            if (sync & !e.Cancel)
            {
                ISO = "";
                foreach (byte b in ISOcod)
                    ISO += b.ToString("X2");
                e.Result = ISO;
                Thread.Sleep(550);
            }
        }

        private void bgwIsoWait_RunWorkerCompleted(object sender, RunWorkerCompletedEventArgs e)
        {
            if (e.Cancelled == true)
            {
                _Disconnected();
                hideUserMessage();
                SetAccelerator(Keys.Escape, null);
            }
            else if (e.Error != null)
            {

            }
            else
            {
                if (e.Result != null)
                    ecu.ISO = (string)e.Result;
                Connected(e.Result != null);
            }
        }

        private void bgwParameters_DoWork(object sender, DoWorkEventArgs e)
        {
            BackgroundWorker worker = (BackgroundWorker)sender;
            byte resp;
            int ComErrCnt = 0;
            byte loopCnt = 0;
            bool isEmpty = true;
            ecu.SetReadTimeout(activeTransport, 300);
            while (!worker.CancellationPending)
            {
                if ((queryFlag & 1) == 1)
                {
                    // Selected Parameters Set Query
                    if (worker.CancellationPending) break;
                    isEmpty = true;
                    for (byte req = 1; req < Request.Length; req++)
                    {
                        if (Request[req])
                        {
                            isEmpty = false;
                            if (ECU.Query(req, out resp))
                            {
                                worker.ReportProgress(49, new queryResult(req, resp, true));
                                ComErrCnt = 0;
                            }
                            else
                            {
                                worker.ReportProgress(49, new queryResult(req, 0, false));
                                ComErrCnt++;
                            }
                            Thread.Sleep(pasvDelay);
                        }
                    }
                    if (!isEmpty)
                        worker.ReportProgress(50, null);
                    else
                        Thread.Sleep(20);
                }
                // Error Set Query
                if ((queryFlag & 2) == 2 & loopCnt == 0)
                {
                    foreach (byte req in ECU.engineErrReq)
                    {
                        if (worker.CancellationPending) break;
                        if (ECU.Query(req, out resp))
                        {
                            worker.ReportProgress(74, new queryResult(req, resp, true));
                            ComErrCnt = 0;
                        }
                        else
                        {
                            worker.ReportProgress(74, new queryResult(req, 0, false));
                            ComErrCnt++;
                        }
                        Thread.Sleep(pasvDelay);
                    }
                    worker.ReportProgress(75, null);
                    // Immo Error Set Query
                    if (ECU.hasIMMO)
                    {
                        foreach (byte req in ECU.immoErrReq)
                        {
                            if (worker.CancellationPending) break;
                            if (ECU.Query(req, out resp))
                            {
                                worker.ReportProgress(99, new queryResult(req, resp, true));
                                ComErrCnt = 0;
                            }
                            else
                            {
                                worker.ReportProgress(99, new queryResult(req, 0, false));
                                ComErrCnt++;
                            }
                            Thread.Sleep(pasvDelay);
                        }
                        worker.ReportProgress(100, null);
                    }
                }
                if (ComErrCnt > 1)
                    throw (new Exception(lang.CommLost));
                else
                    ComErrCnt = 0;
                if (queryFlag == 0)
                    Thread.Sleep(20);
                loopCnt++;
                loopCnt %= 5;
            }
            e.Cancel = true;
        }

        private void bgwParameters_ProgressChanged(object sender, ProgressChangedEventArgs e)
        {
            if (e.ProgressPercentage == 50)
            {
                RefreshData();
            }
            else if (e.ProgressPercentage == 75)
            {
                RefreshEngineErrors();
            }
            else if (e.ProgressPercentage == 100)
            {
                RefreshImmoErrors();
            }
            else
            {
                queryResult QueryResult = (queryResult)e.UserState;
                ecu.Buffer[QueryResult.Request] = QueryResult.Response;
                ecu.Valid[QueryResult.Request] = QueryResult.Success;
            }
        }

        private void bgwParameters_RunWorkerCompleted(object sender, RunWorkerCompletedEventArgs e)
        {
            if (e.Cancelled == true)
            {
                if (DisconnectionPending)
                    Disconnected();
            }
            else if (e.Error != null)
            {
                SetAccelerator(Keys.F12, F12_Reconnect);
                showUserMessage(e.Error.Message, lang.Press_F10);
            }
            else
            {
                // Normal exit - not used
            }
        }

        private bool InitActiveDiag()
        {
            byte Response = 0;
            // Initialize ADM
            if (activeTransport != null && activeTransport.IsOpen)
            {
                return ECU.Query(0xAA, out Response);
            }
            return false;
        }

        private bool ExitActiveDiag()
        {
            byte Response = 0;
            // Finalize ADM
            if (activeTransport != null && activeTransport.IsOpen)
            {
                if (ECU.Query(0xFF, out Response))
                {
                    if (Response == 0xFF)
                        return true;
                }
            }
            return false;
        }

        private void bgwTest_DoWork(object sender, DoWorkEventArgs e)
        {
            while (bgwParameters.IsBusy)
                Thread.Sleep(250); // Wait for the passive diagnostic loop to exit
            BackgroundWorker worker = sender as BackgroundWorker;
            ecu.SetReadTimeout(activeTransport, 500);
            testElement Test = (testElement)e.Argument;
            bool timeout = false;
            byte Response = 0;
            Stopwatch sw = new Stopwatch();

            // Initialize ADM
            if (!InitActiveDiag())
            {
                e.Result = (byte)0xAA; // ADM initialisation FAILED
                return;
            }

            // Send ADM Request
            foreach (byte ActiveDiagCode in Test.RequestSet)
            {
                Thread.Sleep(5);
                timeout = !ECU.Query(ActiveDiagCode, out Response);
                Thread.Sleep(10);
                if ((Response != ActiveDiagCode) | timeout)
                {
                    e.Result = (byte)0xBB; // Test initialisation FAILED
                    ExitActiveDiag();
                    return;
                }
            }
            sw.Start();
            while (activeTransport.BytesToRead == 0 & sw.ElapsedMilliseconds < (Test.TimeOut + 1) * 1000)
            {
                if ((worker.CancellationPending == true))
                {
                    e.Cancel = true;
                    break;
                }
                Thread.Sleep(15); // Wait for Test Result Code
            }
            sw.Stop();
            if (!timeout & !e.Cancel & activeTransport.BytesToRead != 0) // Test completed
            {
                Response = (byte)activeTransport.ReadByte();
                // Test Complete
                e.Result = Response;
                Thread.Sleep(15);
            }
            else // Test cancelled or time-out
            {
                timeout = !ECU.Query(0xFF, out Response); // Abort test
                e.Result = (byte)0xBB;
            }
            // Finalize ADM
            ExitActiveDiag();
        }

        private void bgwTest_RunWorkerCompleted(object sender, RunWorkerCompletedEventArgs e)
        {
            byte Result;
            if (e.Cancelled == true)
            {
                // ADM Cancelled -> PassiveDiag
                hideUserMessage();
                dgvTests.Rows[testIndex].Cells[1].Value = lang.Cancelled;
                bgwParameters.RunWorkerAsync();
            }
            else if (e.Error != null)
            {
                // Caused Error -> PassiveDiag
                hideUserMessage();
                bgwParameters.RunWorkerAsync();
            }
            else
            {
                // ADM Clean Exit -> PassiveDiag
                Result = (byte)e.Result;
                hideUserMessage();
                if (testIndex != -1) // If not CLEAR CODES
                {
                    if (Result == 0xFF) // ... display test result
                        dgvTests.Rows[testIndex].Cells[1].Value = lang.Passed;
                    else if (Result == 0xBB)
                        dgvTests.Rows[testIndex].Cells[1].Value = lang.Timeout;
                    else if (Result == 0xAA)
                        dgvTests.Rows[testIndex].Cells[1].Value = lang.InitFailed;
                    else
                        dgvTests.Rows[testIndex].Cells[1].Value = lang.Failed;
                }
                bgwParameters.RunWorkerAsync();
            }
        }

        private void btnConnect_Click(object sender, EventArgs e)
        {
            if ((ModifierKeys & Keys.Control) == Keys.Control)
            {
                ControlConnectToggle(); // Simulation mode
            }
            else
            {
                ConnectToggle(); // Normal mode
            }
        }

        private void btnCheckAll_Click(object sender, EventArgs e)
        {
            CheckAll();
        }

        private void CheckAll()
        {
            if (ecu.connected & tabControl1.SelectedIndex == 0)
            {
                cblTraces.Items.Clear();
                foreach (DataGridViewRow row in dgvParameters.Rows)
                {
                    row.Cells[0].Value = true;
                    cblTraces.Items.Add(row.Cells[1].Value, false);
                    foreach (byte idx in (byte[])row.Tag)
                    {
                        Request[idx] = true;
                    }
                }
            }
        }

        private void btnUncheckAll_Click(object sender, EventArgs e)
        {
            UncheckAll();
        }

        private void UncheckAll()
        {
            if (ecu.connected & tabControl1.SelectedIndex == 0)
            {
                cblTraces.Items.Clear();
                foreach (DataGridViewRow row in dgvParameters.Rows)
                {
                    row.Cells[0].Value = false;
                    //cblTraces.Items.Remove(row.Cells[1].Value);
                    foreach (byte idx in (byte[])row.Tag)
                    {
                        Request[idx] = false;
                    }
                }
            }
        }

        private void btnClear_Click(object sender, EventArgs e)
        {
            ClearCodes();
        }

        private void ClearCodes()
        {
            if (demo)
            {
            }
            else
            {
                testIndex = -1;
                showUserMessage(lang.ClearingErrors, lang.EnsureIgnOnEngStop);
                bgwParameters.CancelAsync();
                bgwTest.RunWorkerAsync(ECU.clearCodes);
            }
            log.WriteLine();
            log.WriteLine(lang.ClearingErrors);
        }

        private void dgvCOM_CellClick(object sender, DataGridViewCellEventArgs e)
        {
            DataGridView dgv = (DataGridView)sender;
            if (e.RowIndex == -1) return;
            if (e.ColumnIndex == 0)
            {
                string selPort = (string)dgv.Rows[e.RowIndex].Tag;
                serialPort1.PortName = selPort;
                comTransport.PortName = selPort;
                Settings.Default.interfacePort = selPort;
                Settings.Default.Save();
            }
        }

        private void bgwDemo_DoWork(object sender, DoWorkEventArgs e)
        {
            BackgroundWorker worker = (BackgroundWorker)sender;
            while (!worker.CancellationPending)
            {
                if ((queryFlag & 1) == 1)
                {
                    for (int i = 1; i < 16; i++)
                    {
                        ecu.Buffer[i] = (byte)random.Next(255);
                        ecu.Valid[i] = true;
                    }
                    Thread.Sleep(200);
                    worker.ReportProgress(50, null);
                }
                if ((queryFlag & 2) == 2)
                {
                    ecu.Buffer[0x10] = 3;
                    ecu.Buffer[0x14] = 10;
                    ecu.Buffer[0x2E] = 8;
                    Thread.Sleep(75);
                    worker.ReportProgress(75, null);
                    ecu.Buffer[0x71] = 1;
                    Thread.Sleep(25);
                    worker.ReportProgress(100, null);
                }
                Thread.Sleep(25);
            }
            e.Cancel = true;
        }

        private void bgwDemo_ProgressChanged(object sender, ProgressChangedEventArgs e)
        {
            if (e.ProgressPercentage == 50)
            {
                RefreshData();
            }
            else if (e.ProgressPercentage == 75)
            {
                RefreshEngineErrors();
            }
            else if (e.ProgressPercentage == 100)
            {
                RefreshImmoErrors();
            }
        }

        private void bgwDemo_RunWorkerCompleted(object sender, RunWorkerCompletedEventArgs e)
        {
            if (DisconnectionPending)
                Disconnected();
        }

        private void Form1_Resize(object sender, EventArgs e)
        {
            infoPanel.Left = this.ClientSize.Width / 2 - infoPanel.Width / 2;
            infoPanel.Top = this.ClientSize.Height / 2 - infoPanel.Height / 2;
        }

        private void showUserMessage(string message, string options)
        {
            this.Enabled = false;
            infoMessage.Text = message;
            infoOptions.Text = options;
            infoPanel.Visible = true;
            this.Refresh();
        }

        private void hideUserMessage()
        {
            infoPanel.Visible = false;
            this.Enabled = true;
        }

        private void btnAbout_Click(object sender, EventArgs e)
        {
            aboutDialog.ShowDialog(this);
        }

        private void btnExecute_Click(object sender, EventArgs e)
        {
            ExecTest();
        }

        private void ExecTest()
        {
            if (!EnsureSelectedRow(dgvTests, "executing a test"))
                return;
            if (demo)
            {
                testIndex = dgvTests.SelectedRows[0].Index;
                dgvTests.Rows[testIndex].Cells[1].Value = "DEMO";
            }
            else
            {
                testIndex = dgvTests.SelectedRows[0].Index;
                showUserMessage(string.Format(lang.Testing, ((testElement)dgvTests.SelectedRows[0].Tag).Description), string.Format(lang.TestDuration, ((testElement)dgvTests.SelectedRows[0].Tag).TimeOut));
                bgwParameters.CancelAsync();
                bgwTest.RunWorkerAsync((testElement)dgvTests.Rows[testIndex].Tag);
            }
            log.WriteLine();
            log.WriteLine(string.Format(lang.Testing, ((testElement)dgvTests.SelectedRows[0].Tag).Description));
        }

        private void Form1_FormClosing(object sender, FormClosingEventArgs e)
        {
            if (ecu.connected)
            {
                e.Cancel = true;
                Disconnect();
            }
        }

        private void tLog_Tick(object sender, EventArgs e)
        {
            if (log == null)
                return;
            if ((queryFlag & 1) == 1)
            {
                log.WriteLine();
                log.WriteLine(lang.Parameters);
                foreach (DataGridViewRow row in dgvParameters.Rows)
                {
                    if ((bool)row.Cells[0].Value == true)
                        log.WriteLine(row.Cells[1].Value + ": " + row.Cells[2].Value + " " + row.Cells[3].Value);
                }
            }
            if ((queryFlag & 2) == 2)
            {
                log.WriteLine();
                log.WriteLine(lang.Errors);
                foreach (DataGridViewRow row in dgvErrors.Rows)
                {
                    if ((bool)row.Visible == true)
                        log.WriteLine(row.Cells[0].Value + " " + row.Cells[1].Value + " " + row.Cells[2].Value);
                }
            }
        }

        private void Form1_Load(object sender, EventArgs e)
        {
            this.Activate();
            CreateGraph();

            // Initialize COM transport wrapper
            comTransport = new SerialPortTransport(serialPort1);
            activeTransport = comTransport;

            string[] lPorts = System.IO.Ports.SerialPort.GetPortNames();
            Array.Sort(lPorts);
            dgvCOM.Rows.Clear();
            DataGridViewRow dgvRow;
            foreach (string port in lPorts)
            {
                dgvRow = dgvCOM.Rows[dgvCOM.Rows.Add()];
                dgvRow.Cells[0].Value = port;
                dgvRow.Tag = port;
            }
            string pCOM = Settings.Default.interfacePort;
            bool PortExists = false;
            foreach (DataGridViewRow row in dgvCOM.Rows)
                if ((string)row.Cells[0].Value == pCOM)
                {
                    dgvCOM.Rows[row.Index].Cells[0].Selected = true;
                    PortExists = true;
                }
            if (PortExists)
                serialPort1.PortName = pCOM;
            else
            {
                if (lPorts.Length > 0)
                {
                    serialPort1.PortName = lPorts[0];
                    Settings.Default.interfacePort = lPorts[0];
                    Settings.Default.Save();
                }
                else
                {
                    serialPort1.PortName = "COM0";
                }
                MessageBox.Show(string.Format(lang.PreviouslySelectedPort, pCOM), lang.COMnotValid, MessageBoxButtons.OK, MessageBoxIcon.Warning);
            }
            cbLOG.Checked = Settings.Default.logEnabled;
            cbCompat.Checked = Settings.Default.compatEnabled;

            // Restore BLE mode if previously selected
            if (Settings.Default.bleModeEnabled)
                rbBLE.Checked = true; // triggers rbBLE_CheckedChanged which sets activeTransport

            ecuList = new string[,] { { "auto", lang.autodetect, code.GetCars() }, { iaw16f.name, iaw16f.longName, iaw16f.GetCars() }, { iaw18f.name, iaw18f.longName, iaw18f.GetCars() }, { iaw18fd.name, iaw18fd.longName, iaw18fd.GetCars() }, { iaw8f_68.name, iaw8f_68.longName, iaw8f_68.GetCars() }, { iaw04k.name, iaw04k.longName, iaw04k.GetCars() }, { code.name, code.longName, code.GetCars() } };
            for (int i = 0; i < ecuList.GetLength(0) ; i++)
            {
                dgvRow = dgvECU.Rows[dgvECU.Rows.Add()];
                dgvRow.Cells[0].Value = ecuList[i, 1];
                dgvRow.Tag = ecuList[i, 0];
                dgvRow.Cells[0].ToolTipText = ecuList[i, 2]; ;
            }
        }

        private void cbLOG_CheckedChanged(object sender, EventArgs e)
        {
            Settings.Default.logEnabled = cbLOG.Checked;
            Settings.Default.Save();
        }

        #region Bluetooth LE UI

        private void InitializeBleUI()
        {
            bleDevicesFound = new Dictionary<ulong, string>();

            // --- Radio button panel (COM vs BLE toggle) ---
            Panel pnlConnectionMode = new Panel();
            pnlConnectionMode.Height = 28;
            pnlConnectionMode.Dock = DockStyle.Top;

            rbCOM = new RadioButton();
            rbCOM.Text = "COM";
            rbCOM.AutoSize = true;
            rbCOM.Checked = true;
            rbCOM.Location = new Point(4, 5);

            rbBLE = new RadioButton();
            rbBLE.Text = "Bluetooth LE";
            rbBLE.AutoSize = true;
            rbBLE.Location = new Point(78, 5);

            pnlConnectionMode.Controls.Add(rbCOM);
            pnlConnectionMode.Controls.Add(rbBLE);

            // --- BLE device list panel (initially hidden) ---
            pnlBLE = new Panel();
            pnlBLE.Dock = DockStyle.Fill;
            pnlBLE.Visible = false;

            btnScanBLE = new Button();
            btnScanBLE.Text = "Scansiona BLE";
            btnScanBLE.Dock = DockStyle.Bottom;
            btnScanBLE.Height = 26;

            dgvBLE = new DataGridView();
            dgvBLE.Dock = DockStyle.Fill;
            dgvBLE.AllowUserToAddRows = false;
            dgvBLE.AllowUserToDeleteRows = false;
            dgvBLE.AllowUserToResizeColumns = false;
            dgvBLE.AllowUserToResizeRows = false;
            dgvBLE.ColumnHeadersHeightSizeMode = DataGridViewColumnHeadersHeightSizeMode.AutoSize;
            dgvBLE.ColumnHeadersVisible = false;
            dgvBLE.MultiSelect = false;
            dgvBLE.ReadOnly = true;
            dgvBLE.RowHeadersVisible = false;
            dgvBLE.RowHeadersWidthSizeMode = DataGridViewRowHeadersWidthSizeMode.DisableResizing;
            dgvBLE.SelectionMode = DataGridViewSelectionMode.FullRowSelect;
            dgvBLE.EditMode = DataGridViewEditMode.EditProgrammatically;
            bleName = new DataGridViewTextBoxColumn();
            bleName.Name = "bleName";
            bleName.AutoSizeMode = DataGridViewAutoSizeColumnMode.Fill;
            bleName.ReadOnly = true;
            dgvBLE.Columns.Add(bleName);

            pnlBLE.Controls.Add(dgvBLE);
            pnlBLE.Controls.Add(btnScanBLE);

            // --- Integrate into existing layout ---
            // Create a container panel to hold both dgvCOM and pnlBLE (toggled by visibility)
            Panel pnlContent = new Panel();
            pnlContent.Dock = DockStyle.Fill;
            tableLayoutPanel7.Controls.Remove(dgvCOM);
            dgvCOM.Dock = DockStyle.Fill;
            pnlContent.Controls.Add(dgvCOM);
            pnlContent.Controls.Add(pnlBLE);
            tableLayoutPanel7.Controls.Add(pnlContent, 0, 0);

            // Ensure tableLayoutPanel7 fills groupBox2 beneath the radio button row
            tableLayoutPanel7.Dock = DockStyle.Fill;

            // Add the radio button panel at the top of groupBox2
            groupBox2.Controls.Add(pnlConnectionMode);

            // --- Wire up events AFTER all controls are created ---
            rbCOM.CheckedChanged += new EventHandler(rbCOM_CheckedChanged);
            rbBLE.CheckedChanged += new EventHandler(rbBLE_CheckedChanged);
            btnScanBLE.Click += new EventHandler(async (s, e) => await btnScanBLE_Click(s, e));
            dgvBLE.CellClick += new DataGridViewCellEventHandler(dgvBLE_CellClick);
        }

        private void rbCOM_CheckedChanged(object sender, EventArgs e)
        {
            if (!rbCOM.Checked) return;
            pnlBLE.Visible = false;
            dgvCOM.Visible = true;
            if (comTransport != null)
                activeTransport = comTransport;
            Settings.Default.bleModeEnabled = false;
            Settings.Default.Save();
        }

        private void rbBLE_CheckedChanged(object sender, EventArgs e)
        {
            if (!rbBLE.Checked) return;
            dgvCOM.Visible = false;
            pnlBLE.Visible = true;
            Settings.Default.bleModeEnabled = true;
            Settings.Default.Save();
            // Restore previously selected BLE device if one was saved
            string savedAddr = Settings.Default.bleDeviceAddress;
            if (!string.IsNullOrEmpty(savedAddr) && ulong.TryParse(savedAddr, out ulong addr))
            {
                string name = Settings.Default.bleDeviceName;
                if (string.IsNullOrEmpty(name)) name = addr.ToString("X12");
                activeTransport = new BleSerialTransport(addr, name);
                // Register in the found devices dict and display in the list
                lock (bleDevicesFound) { bleDevicesFound[addr] = name; }
                dgvBLE.Rows.Clear();
                DataGridViewRow row = dgvBLE.Rows[dgvBLE.Rows.Add()];
                row.Cells[0].Value = name + " ✓";
                row.Tag = addr;
                dgvBLE.Rows[0].Selected = true;
            }
            else
            {
                activeTransport = null;
            }
        }

        private async Task<bool> BLEAvailable()
        {
            try
            {
                var selector = BluetoothAdapter.GetDeviceSelector();
                var devices = await Windows.Devices.Enumeration.DeviceInformation.FindAllAsync(selector);
                foreach (var device in devices)
                {
                    var adapter = await BluetoothAdapter.FromIdAsync(device.Id);
                    if (adapter != null && adapter.IsLowEnergySupported)
                        return true;
                }
                return false;
            }
            catch
            {
                return false;
            }
        }

        private async Task btnScanBLE_Click(object sender, EventArgs e)
        {

            if (await BLEAvailable() == false)
            {
                MessageBox.Show("Bluetooth LE non è supportato su questo dispositivo.", "BLE non supportato", MessageBoxButtons.OK, MessageBoxIcon.Error);
                return;
            }

            if (bleWatcher != null && bleWatcher.Status == BluetoothLEAdvertisementWatcherStatus.Started)
            {
                bleWatcher.Stop();
                btnScanBLE.Text = "Scansiona BLE";
                return;
            }

            bleDevicesFound.Clear();
            dgvBLE.Rows.Clear();
            btnScanBLE.Text = "Ferma scansione";

            bleWatcher = new BluetoothLEAdvertisementWatcher();
            bleWatcher.ScanningMode = BluetoothLEScanningMode.Active;
            bleWatcher.Received += BleWatcher_Received;
            bleWatcher.Stopped  += BleWatcher_Stopped;
            bleWatcher.Start();
        }

        private void BleWatcher_Received(BluetoothLEAdvertisementWatcher sender,
                                          BluetoothLEAdvertisementReceivedEventArgs args)
        {
            ulong addr = args.BluetoothAddress;
            string name = args.Advertisement.LocalName;
            if (string.IsNullOrWhiteSpace(name))
                name = addr.ToString("X12");

            bool isNew;
            lock (bleDevicesFound)
            {
                isNew = !bleDevicesFound.ContainsKey(addr);
                if (isNew) bleDevicesFound[addr] = name;
            }

            if (isNew)
            {
                BeginInvoke((Action)(() =>
                {
                    DataGridViewRow row = dgvBLE.Rows[dgvBLE.Rows.Add()];
                    row.Cells[0].Value = name;
                    row.Tag = addr;
                }));
            }
        }

        private void BleWatcher_Stopped(BluetoothLEAdvertisementWatcher sender,
                                         BluetoothLEAdvertisementWatcherStoppedEventArgs args)
        {
            BeginInvoke((Action)(() =>
            {
                btnScanBLE.Text = "Scansiona BLE";
                if (args.Error != BluetoothError.Success)
                {
                    string msg = $"BLE Watcher fermato con errore: {args.Error}";
                    // In alternativa:
                    MessageBox.Show(msg, "Errore BLE", MessageBoxButtons.OK, MessageBoxIcon.Error);
                    // oppure log su Debug/Output:
                    // System.Diagnostics.Debug.WriteLine(msg);
                }
            }));
        }



        private void dgvBLE_CellClick(object sender, DataGridViewCellEventArgs e)
        {
            if (e.RowIndex < 0 || e.ColumnIndex < 0) return;
            DataGridViewRow row = dgvBLE.Rows[e.RowIndex];
            if (row.Tag == null) return;

            ulong addr = (ulong)row.Tag;
            string name;
            lock (bleDevicesFound)
            {
                if (!bleDevicesFound.TryGetValue(addr, out name))
                    name = addr.ToString("X12");
            }

            // Close previous BLE transport if open
            if (activeTransport is BleSerialTransport && activeTransport.IsOpen)
                activeTransport.Close();

            activeTransport = new BleSerialTransport(addr, name);
            Settings.Default.bleDeviceAddress = addr.ToString();
            Settings.Default.bleDeviceName    = name;
            Settings.Default.Save();
        }

        #endregion

        private void btnGraph_Click(object sender, EventArgs e)
        {
            RecordToggle();
        }

        private void RecordToggle()
        {
            if (tabControl1.SelectedIndex != 3) return;
            if (!recording)// If not currently recording
            {
                cblTraces.Enabled = false;
                btnGraph.Text = lang.Stop;
                recording = true;
                tGraph.Start();
                time.Start();
                exp = new StreamWriter(logDir + @"\IESexp_" + DateTime.Now.ToString("yyMMddHHmm") + ".csv", true);
                exp.Write("Timestamp");
                string[] traces = new string[cblTraces.CheckedItems.Count];
                int j = 0;
                string trace = "";
                for (int i = 0; i < ECU.engineData.Length; i++)
                {
                    if ((bool)dgvParameters.Rows[i].Cells[0].Value == true)
                    {
                        if (cblTraces.GetItemChecked(cblTraces.Items.IndexOf(dgvParameters.Rows[i].Cells[1].Value)))
                        {
                            trace = ECU.engineData[i].Description;
                            exp.Write(";" + trace);
                            traces[j++] = trace;
                        }
                    }
                }
                exp.WriteLine("");
                InitCurves(traces);
            }
            else
            {
                exp.Close();
                btnGraph.Text = lang.Start;
                tGraph.Stop();
                recording = false;
                time.Reset();
                cblTraces.Enabled = true;
            }
        }

        private void tabControl1_Selecting(object sender, TabControlCancelEventArgs e)
        {
            e.Cancel = recording;
        }

        private void CreateGraph()
        {
            GraphPane myPane = zedGraphControl1.GraphPane;
            // Set the titles and axis labels
            myPane.Title.IsVisible = false;
            myPane.XAxis.Title.IsVisible = false;
            myPane.YAxis.Title.IsVisible = false;
        }

        private void InitCurves(string[] traces)
        {
            GraphPane myPane = zedGraphControl1.GraphPane;
            myPane.CurveList.Clear();
            foreach (string trace in traces)
            {
                myPane.AddCurve(trace, null, null, Color.FromArgb(random.Next(0, 255), random.Next(0, 255), random.Next(0, 255)), SymbolType.None).Line.Width = 2.0F;
            }
            zedGraphControl1.AxisChange();
            zedGraphControl1.Refresh();
        }

        private void AddDataToGraph(double xValue, double[] yValues)
        {
            GraphPane myPane = zedGraphControl1.GraphPane;
            // Make sure that the curvelist has at least one curve
            if (myPane.CurveList.Count <= 0)
                return;
            else if (myPane.CurveList.Count != yValues.Length)
                return;

            for (int i = 0; i < yValues.Length; i++)
            {
                ((IPointListEdit)myPane.CurveList[i].Points).Add(xValue, yValues[i]);
            }
            // force redraw
            this.Invoke(RDC);
        }

        private void AddDataToGraph(double xValue, double yValue, int TraceIdx)
        {
            GraphPane myPane = zedGraphControl1.GraphPane;
            // Make sure that the curvelist has at least one curve
            if (myPane.CurveList.Count <= 0)
                return;

            ((IPointListEdit)myPane.CurveList[TraceIdx].Points).Add(xValue, yValue);

            // force redraw
            this.Invoke(RDC);
        }

        private delegate void RedrawGraphCallback();

        private void RedrawGraph()
        {
            zedGraphControl1.AxisChange();
            zedGraphControl1.Refresh();
        }

        private void EscCancelConnect()
        {
            bgwIsoWait.CancelAsync();
        }

        private void cbCompat_CheckedChanged(object sender, EventArgs e)
        {
            if (cbCompat.Checked)
                pasvDelay = 20;
            else
                pasvDelay = 4;

            Settings.Default.compatEnabled = cbCompat.Checked;
            Settings.Default.Save();
        }

        private void tabControl1_Selected(object sender, TabControlEventArgs e)
        {
            if (tabControl1.SelectedIndex == -1 | tabControl1.SelectedIndex == 0 | tabControl1.SelectedIndex == 3) // Parameters or Graph Tab
                if (cbQueryErrors.Checked)
                    queryFlag = 3;
                else
                    queryFlag = 1;
            else if (tabControl1.SelectedIndex == 1) // Errors Tab
                queryFlag = 2;
            else
                queryFlag = 0;
        }

        private void cbQueryErrors_CheckedChanged(object sender, EventArgs e)
        {
            if (cbQueryErrors.Checked)
                queryFlag = 3;
            else
                queryFlag = 1;
        }

        private void tGraph_Tick(object sender, EventArgs e)
        {
            exp.Write(time.ElapsedMilliseconds);
            double Value;
            double[] yValues = new double[cblTraces.CheckedItems.Count];
            int j = 0;
            for (int i = 0; i < ECU.engineData.Length; i++)
            {
                if ((bool)dgvParameters.Rows[i].Cells[0].Value == true)
                {
                    if (cblTraces.GetItemChecked(cblTraces.Items.IndexOf(dgvParameters.Rows[i].Cells[1].Value)))
                    {
                        Value = (double)ECU.engineData[i].Value;
                        exp.Write(";" + Value);
                        yValues[j++] = Value;
                    }
                }
            }
            //TODO: Create an array of indices of selected parameters in ECU.engineData[] instead of re-evaluating whole list in each loop
            exp.WriteLine("");
            AddDataToGraph(time.ElapsedMilliseconds / 1000.0, yValues);
            zedGraphControl1.GraphPane.XAxis.Scale.Max = time.ElapsedMilliseconds / 1000.0;
            zedGraphControl1.GraphPane.XAxis.Scale.Min = zedGraphControl1.GraphPane.XAxis.Scale.Max - 15;
        }

        private void btnAdj_Click(object sender, EventArgs e)
        {
            ExecAdjustment();
        }

        private void ExecAdjustment()
        {
            if (!EnsureSelectedRow(dgvAdjusts, "adjusting"))
                return;
            if (demo)
            {
                adjIndex = dgvAdjusts.SelectedRows[0].Index;
                dgvAdjusts.Rows[adjIndex].Cells[1].Value = "DEMO";
            }
            else
            {
                bool timeout = false;
                byte Response;
                byte Status = 0;
                adjIndex = dgvAdjusts.SelectedRows[0].Index;
                dgvAdjusts.Rows[adjIndex].Cells[1].Value = lang.InProceed;
                adjustElement Adjust = (adjustElement)dgvAdjusts.Rows[adjIndex].Tag;
                bgwParameters.CancelAsync();
                while (bgwParameters.IsBusy)
                {
                    Application.DoEvents();
                    Thread.Sleep(250); // Wait for the passive diagnostic loop to exit
                }
                if (activeTransport != null && activeTransport.IsOpen)
                {
                    ecu.SetReadTimeout(activeTransport, 300);
                    // Read Value if available
                    if (Adjust.StatusByte != 0x00)
                    {
                        timeout = !ECU.Query(Adjust.StatusByte, out Response);
                        if (timeout)
                        {
                            dgvAdjusts.Rows[adjIndex].Cells[1].Value = lang.InitFailed; // Test initialisation FAILED
                            ExitActiveDiag();
                            if (!bgwParameters.IsBusy) bgwParameters.RunWorkerAsync();
                            return;
                        }
                        Status = Response;
                    }
                    // Initialize ADM
                    if (!InitActiveDiag())
                    {
                        dgvAdjusts.Rows[adjIndex].Cells[1].Value = lang.InitFailed; // ADM initialisation FAILED
                        return;
                    }
                    // Send ADM Request
                    foreach (byte AdjustInitCode in Adjust.PreSet)
                    {
                        Thread.Sleep(5);
                        timeout = !ECU.Query(AdjustInitCode, out Response);
                        if ((Response != AdjustInitCode) | timeout)
                        {
                            dgvAdjusts.Rows[adjIndex].Cells[1].Value = lang.InitFailed; // Test initialisation FAILED
                            ExitActiveDiag(); // Abort current test
                            ExitActiveDiag();
                            if (!bgwParameters.IsBusy) bgwParameters.RunWorkerAsync();
                            return;
                        }
                    }
                    // Send additional data if adequate
                    switch (Adjust.Type)
                    {
                        case "toggle":
                            break;
                        case "onebyte":
                            FrmOneByte.lblAdjTitle.Text = Adjust.Description;
                            FrmOneByte.tbAdjTrack.Value = Status;
                            FrmOneByte.ShowDialog(this);
                            break;
                        case "code":
                            //TODO: code transmit
                            break;
                    }
                    // Send PostCode
                    if (Adjust.PostSet != null)
                    {
                        foreach (byte Code in Adjust.PostSet)
                        {
                            Thread.Sleep(5);
                            timeout = !ECU.Query(Code, out Response);
                            if ((Response != Code) | timeout)
                            {
                                dgvAdjusts.Rows[adjIndex].Cells[1].Value = lang.Failed; // Test FAILED
                                ExitActiveDiag(); // Abort current test
                                ExitActiveDiag();
                                if (!bgwParameters.IsBusy) bgwParameters.RunWorkerAsync();
                                return;
                            }
                        }
                    }
                    // Finalize ADM
                    ExitActiveDiag();
                    dgvAdjusts.Rows[adjIndex].Cells[1].Value = lang.Passed;
                    if (!bgwParameters.IsBusy) bgwParameters.RunWorkerAsync();
                }
            }
            log.WriteLine();
            log.WriteLine(string.Format(lang.Adjusting, ((adjustElement)dgvAdjusts.SelectedRows[0].Tag).Description));
        }
    }
}
