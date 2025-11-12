using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Data;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Forms; 
using InTheHand.Net;
using InTheHand.Net.Bluetooth;
using InTheHand.Net.Sockets; 
using Windows.Devices.Bluetooth;
using Windows.Devices.Bluetooth.Advertisement;
using Windows.Devices.Enumeration;
using Windows.Foundation;
using Windows.Storage.Streams;

namespace UnlockServer
{
    public partial class Form1 : Form
    {
        BluetoothDiscover bluetoothDiscover;
        // 新增：锁定延迟相关变量
        private int lockDelaySeconds = 5; // 默认延迟5秒
        private DateTime? signalLossStartTime = null;
        private bool isWaitingForLock = false;
        private bool lockNotificationSent = false;

        public Form1()
        {
            InitializeComponent();
            this.Load += Form1_Load; 
            var softwareVersion = System.Reflection.Assembly.GetExecutingAssembly().GetName().Version.ToString();
            this.Text = "蓝牙解锁工具_v" + softwareVersion;
            this.notifyIcon1.Visible = true;
            this.FormClosing += Form1_FormClosing;
            this.FormClosed += Form1_FormClosed;

            // 新增：添加延迟设置控件
            AddDelayControl();
        }

        // 新增：添加延迟设置控件
        private void AddDelayControl()
        {
            Label lblDelay = new Label();
            lblDelay.Text = "锁定延迟(秒):";
            lblDelay.Location = new System.Drawing.Point(307, 293);
            lblDelay.Size = new System.Drawing.Size(80, 20);
            this.Controls.Add(lblDelay);

            NumericUpDown numDelay = new NumericUpDown();
            numDelay.Name = "numDelay";
            numDelay.Location = new System.Drawing.Point(393, 291);
            numDelay.Size = new System.Drawing.Size(60, 22);
            numDelay.Minimum = 1;
            numDelay.Maximum = 30;
            numDelay.Value = lockDelaySeconds;
            numDelay.ValueChanged += NumDelay_ValueChanged;
            this.Controls.Add(numDelay);
        }

        // 新增：延迟时间变更事件
        private void NumDelay_ValueChanged(object sender, EventArgs e)
        {
            NumericUpDown numDelay = sender as NumericUpDown;
            if (numDelay != null)
            {
                lockDelaySeconds = (int)numDelay.Value;
                OperateIniFile.WriteIniInt("setting", "lockdelay", lockDelaySeconds);
            }
        }

        private void Form1_FormClosing(object sender, FormClosingEventArgs e)
        {
            if (e.CloseReason == CloseReason.ApplicationExitCall)
            {  
                    this.notifyIcon1.Visible = false;
                    Application.Exit(); 
            }
            else
            {
                e.Cancel = true;
                this.WindowState = FormWindowState.Minimized;
                this.ShowInTaskbar = false;
                this.Hide(); 
            }
        }

        private void Form1_FormClosed(object sender, FormClosedEventArgs e)
        {
            try
            {
                isrunning = false;
                sessionSwitchClass.Close();
                bluetoothDiscover?.StopDiscover(); 
            }
            catch (Exception ex)
            {

            }
        }
        private const string SignalStrengthProperty = "System.Devices.Aep.SignalStrength";

        private static SessionSwitchClass sessionSwitchClass;
        private void Form1_Load(object sender, EventArgs e)
        {
            sessionSwitchClass  = new SessionSwitchClass(); 
            try
            {
                loadConfig(); 
                bluetoothDiscover = new BluetoothDiscover(bletype);
                bluetoothDiscover.StartDiscover();

                BluetoothRadio radio = BluetoothRadio.Default;//获取蓝牙适配器
                if (radio == null)
                { 
                    MessageBox.Show("没有找到本机蓝牙设备！");
                    return;
                } 
                btn_refreshbluetooth_Click(null, null);

                // 新增：从配置加载延迟时间
                var delayControl = this.Controls["numDelay"] as NumericUpDown;
                if (delayControl != null)
                {
                    delayControl.Value = lockDelaySeconds;
                }

                Task.Delay(3000).ContinueWith((r) =>
                {
                    isrunning = true;
                    while (isrunning)
                    {
                        try
                        {
                            Tick();
                        }catch(Exception ex)
                        {
                            Console.WriteLine("error:"+ex.Message);
                        }
                        Thread.Sleep(1000);
                    }

                },TaskContinuationOptions.LongRunning);

            }
            catch(Exception ex)
            {
                MessageBox.Show("启动蓝牙监控失败，可能没有蓝牙硬件或者不兼容！");
            } 
            if (Program.ishideRun)
            { 
                this.Close();
            }
        }
        bool isrunning = false;
        private void MWatcher_Updated(DeviceWatcher sender, DeviceInformationUpdate deviceInformation)
        {
            var rssi = Convert.ToInt16(deviceInformation.Properties[SignalStrengthProperty]);
            Console.WriteLine(rssi + " " + deviceInformation.Id);
        }

        private void MWatcher_Added(DeviceWatcher sender, DeviceInformation deviceInformation)
        {

            var rssi = Convert.ToInt16(deviceInformation.Properties[SignalStrengthProperty]);
            Console.WriteLine(rssi + " " + deviceInformation.Id);

        }

        int rssiyuzhi = -90;

        private void loadConfig()
        {

            checkBox1.Checked = AutoStartHelper.IsExists();

            txtip.Text = OperateIniFile.ReadSafeString("setting", "ip", txtip.Text);
            txtpt.Text = OperateIniFile.ReadSafeString("setting", "pt", txtpt.Text);
            txtus.Text = OperateIniFile.ReadSafeString("setting", "us", txtus.Text); 
            txtpd.Text = OperateIniFile.ReadSafeString("setting", "pd", txtpd.Text);
            txtrssi.Text = OperateIniFile.ReadSafeString("setting", "rssi", txtrssi.Text);
            int.TryParse(txtrssi.Text, out rssiyuzhi);
            unlockaddress = OperateIniFile.ReadSafeString("setting", "address", unlockaddress); 
            getBluetoothType();
            reloadLockConfig(); 
            WanClient.reloadConfig();

            // 新增：加载延迟时间配置
            lockDelaySeconds = OperateIniFile.ReadIniInt("setting", "lockdelay", 5);
        }

        private int bletype = 1;
        private void getBluetoothType()
        {
            var type= OperateIniFile.ReadIniInt("setting", "type", 1);
            bletype = type;
             if (type == 2)
            {
                rdbble.Checked = true;
            }
            else
            {
                rdbclassic.Checked = true;
            }
        }

        private void setBluetoothType()
        {
            int type = 0;
            if (rdbclassic.Checked)
            {
                type = 1;
            }
            if(rdbble.Checked)
            {
                type = 2;
            }
            bletype = type;
            OperateIniFile.WriteIniInt("setting", "type", type);
        }


        string unlockaddress = "";
        private void btn_save_Click(object sender, EventArgs e)
        { 
            try
            {
                if (string.IsNullOrEmpty(txtip.Text))
                {
                    txtip.Focus();
                    MessageBox.Show(this, "IP地址不能为空！");
                    return;
                }
                if (string.IsNullOrEmpty(txtus.Text))
                {
                    txtus.Focus();
                    MessageBox.Show(this, "账号不能为空！");
                    return;
                }

                if (string.IsNullOrEmpty(txtpd.Text))
                {
                    txtpd.Focus();
                    MessageBox.Show(this, "密码不能为空！");
                    return;
                }

                if (lstbldevice.SelectedIndex < 0 || lstbldevice.SelectedIndex >= lstbldevice.Items.Count)
                {
                    MessageBox.Show(this, "请选择有效的蓝牙设备!");
                    return;
                }

                var selectedDevice = deviceAddresses[lstbldevice.Items[lstbldevice.SelectedIndex].ToString()];
                var address = selectedDevice.Address;
                OperateIniFile.WriteSafeString("setting", "address", address);
                unlockaddress = address;

                if (int.TryParse(txtrssi.Text, out rssiyuzhi) == false || rssiyuzhi > 0 || rssiyuzhi < -128)
                {
                    txtrssi.Focus();
                    MessageBox.Show(this, "请输入有效的信号阈值(-128到0)!");
                    return;
                }
                if (int.TryParse(txtpt.Text, out int pt) == false || pt >= 65565 || pt <= 0)
                {
                    txtrssi.Focus();
                    MessageBox.Show(this, "请输入有效的端口号(1到65564)!");
                    return;
                }

                // 新增：保存延迟时间配置
                var delayControl = this.Controls["numDelay"] as NumericUpDown;
                if (delayControl != null)
                {
                    lockDelaySeconds = (int)delayControl.Value;
                    OperateIniFile.WriteIniInt("setting", "lockdelay", lockDelaySeconds);
                }

                OperateIniFile.WriteSafeString("setting", "ip", txtip.Text);
                OperateIniFile.WriteSafeString("setting", "pt", txtpt.Text);
                OperateIniFile.WriteSafeString("setting", "us", txtus.Text);
                OperateIniFile.WriteSafeString("setting", "pd", txtpd.Text);
                OperateIniFile.WriteSafeString("setting", "rssi", txtrssi.Text);
                setBluetoothType();
                WanClient.reloadConfig();

                MessageBox.Show(this, "保存成功！");
            }catch(Exception ex)
            { 
                MessageBox.Show(this, "保存配置发生错误！");
            }
        }

        Dictionary<string, short> catchedDeviceRssi = new Dictionary<string, short>();
        private void OnAdvertisementReceived(BluetoothLEAdvertisementWatcher watcher, BluetoothLEAdvertisementReceivedEventArgs eventArgs)
        {
            short rssi = eventArgs.RawSignalStrengthInDBm;
            var address = new BluetoothAddress(eventArgs.BluetoothAddress).ToString();
            Console.WriteLine(address + "-----" + rssi);
            if (rssi <= -120)
            {
                if(catchedDeviceRssi.ContainsKey(address))
                {
                    catchedDeviceRssi.Remove(address);
                }
            }
            else
            { 
                catchedDeviceRssi[address] = rssi;
            }
        } 

        private void DeviceWatcher_Added(DeviceWatcher sender, DeviceInformation args)
        {
            if (string.IsNullOrEmpty(args.Name)) { return; }

            var rssi = args.Properties.Single(d => d.Key == "System.Devices.Aep.SignalStrength").Value;
            if (rssi == null) { return; }
            int x1 = int.Parse(rssi.ToString());

            if (x1 < -80) { return; }

            var id = args.Id;
            string s1 = $"[{x1}] {(args.Name).PadLeft(30, ' ')} {args.Id}";
             
        }


        private void btn_refreshbluetooth_Click(object sender, EventArgs e)
        {
            Task.Run(() =>
            { 
                try
                {
                    this.Invoke(new Action(() =>
                    {
                        btn_refreshbluetooth.Text = "正在刷新";
                        btn_refreshbluetooth.Enabled = false;
                    }));
               
                    LoadList();


                    this.Invoke(new Action(() =>
                    {
                        btn_refreshbluetooth.Text = "刷新蓝牙设备";
                        btn_refreshbluetooth.Enabled = true;
                    }));

                
                }
                catch (Exception ex)
                {
                    Console.WriteLine(ex.Message);
                    this.Invoke(new Action(() =>
                    {
                        MessageBox.Show(this,"未找到本机蓝牙设备!");
                    }));
                } 
            });
        }
        Dictionary<string, MybluetoothDevice> deviceAddresses = new Dictionary<string, MybluetoothDevice>();
        private void LoadList()
        {
            if(bluetoothDiscover!=null)
            {
                bluetoothDiscover.StopDiscover();
            } 
            bluetoothDiscover = new BluetoothDiscover(bletype);
            bluetoothDiscover.StartDiscover();
             
            var address = OperateIniFile.ReadSafeString("setting", "address", ""); 
             
            Thread.Sleep(2000);

            var devices = bluetoothDiscover.getAllDevice();

            List<string> items = new List<string>(); 
            this.Invoke(new Action(() =>
            {
                lstbldevice.Items.Clear();
            })); 

            deviceAddresses.Clear();
            int index = -1;
            int i = 0;
            lock(BluetoothDiscover.locker)
            {
                foreach (MybluetoothDevice device in devices)
                {
                    var inlist = device.Name + "(" + device.Type + ")" + device.Rssi + "dBm" + "[" + device.Address + "]";
                    this.Invoke(new Action(() =>
                    {
                        lstbldevice.Items.Add(inlist);
                    }));
                    if (address == device.Address)
                    {
                        index = i;
                    }
                    deviceAddresses[inlist] = device;
                    i++;
                }
            }
          
            this.Invoke(new Action(() =>
            {
                if (lstbldevice.Items.Count > 0)
                {
                    lstbldevice.SelectedIndex = index;
                } 
            }));
           
        }

        private void button1_Click(object sender, EventArgs e)
        {
            Process.Start(new ProcessStartInfo("https://www.52pojie.cn/thread-1678522-1-1.html"));  
        }

        private void notifyIcon1_MouseDoubleClick(object sender, MouseEventArgs e)
        {
            if(e.Button == MouseButtons.Left)
            {
                this.Show();
                this.ShowInTaskbar = true;
                this.WindowState = FormWindowState.Normal;
                this.TopMost = true;
                this.TopMost = false; 
            }
        }

        private void 显示ToolStripMenuItem_Click(object sender, EventArgs e)
        {
            this.Show();
            this.ShowInTaskbar = true;
            this.WindowState = FormWindowState.Normal;
            this.TopMost = true;
            this.TopMost = false; 
        }

        private void 退出ToolStripMenuItem_Click(object sender, EventArgs e)
        {
            if (MessageBox.Show("是否退出？", "提示", MessageBoxButtons.YesNo, MessageBoxIcon.Question) != DialogResult.Yes)
            {
                return;
            }
            else
            {
                this.notifyIcon1.Visible = false; 
                Application.Exit();
            }
        } 

        private int locktimecount = 0; 
        private bool isunlockfail = false;

        private object lockLock = new object();

        /// <summary>
        /// 锁定超时，默认60秒最多锁定一次，防止找不到设备重复锁定导致电脑无法解锁
        /// </summary>
        private TimeSpan LockTimeOut = TimeSpan.FromMilliseconds(60 * 1000);

        /// <summary>
        /// 解锁超时，默认60秒最多解锁一次
        /// </summary>
        private TimeSpan UnLockTimeOut = TimeSpan.FromMilliseconds(30 * 1000);

        DateTime lastLockTime = DateTime.MinValue;
        DateTime lastUnLockTime = DateTime.MinValue;

        /// <summary>
        /// 超时锁定
        /// </summary>
        private void LockByTimeOut()
        {
            DateTime now = DateTime.Now;

            if((now - lastLockTime) > LockTimeOut )
            {
                //这里判断时间是否超过 LockTimeOut
                lastLockTime = DateTime.Now;
                WanClient.LockPc();
            }  
        }

        /// <summary>
        /// 超时锁定
        /// </summary>
        private bool UnLockByTimeOut()
        {
            DateTime now = DateTime.Now;

            if ((now - lastUnLockTime) > UnLockTimeOut)
            {
                //这里判断时间是否超过 UnLockTimeOut
                lastUnLockTime = DateTime.Now;
                return WanClient.UnlockPc();
            }
            return true;
        }

        // 新增：发送锁定通知
        private void ShowLockNotification(int remainingSeconds)
        {
            if (remainingSeconds > 0)
            {
                notifyIcon1.ShowBalloonTip(1000, "即将锁定", 
                    $"蓝牙信号弱或丢失，将在 {remainingSeconds} 秒后锁定电脑...", 
                    ToolTipIcon.Warning);
            }
        }

        private void Tick()
        {
            if(isautolock == false && isautounlock == false)
            {
                //没有启用
                Console.WriteLine("未启用");
                return;
            }
            if (string.IsNullOrWhiteSpace(unlockaddress) || WanClient.isConfigVal() == false) 
            {
                //配置无效
                Console.WriteLine("配置无效");
                return;
            }

            lock (lockLock)
            {
                bool islocked = WanClient.IsSessionLocked();
            
                if(islocked)
                {
                    //现在是锁定状态 
                    // 重置锁定延迟计时器
                    ResetLockDelayTimer();
                }
                else
                {
                    //已经解锁 
                    isunlockfail = false;
                }

                if(isunlockfail)
                {
                    //上次解锁失败
                    locktimecount++;
                    return;
                }
                if (locktimecount >= 120)
                {
                    //重置时间
                    isunlockfail = false;
                    locktimecount = 0; 
                }

                if(bluetoothDiscover==null)
                {
                    return;
                }
                var Devices = bluetoothDiscover.getAllDevice();
               
                MybluetoothDevice device = Devices.FirstOrDefault(p => p.Address == unlockaddress);
                bool signalValid = false;
                
                if (device != null)
                {
                    Console.WriteLine("发现设备:" + device.Name + "[" + device.Address + "] " + device.Rssi+"dBm");
                    if (device.Rssi >= rssiyuzhi)
                    {
                        signalValid = true;
                    }
                }

                // 信号有效，重置计时器
                if (signalValid)
                {
                    ResetLockDelayTimer();
                    if (islocked && isautounlock)
                    {
                        if (manuallock && sessionSwitchClass.isLockBySoft == false)
                        {
                            Console.WriteLine("非软件锁定，不干预！");
                            return;
                        }
                        Console.WriteLine("信号强度够且处于锁屏状态，解锁！");
                        sessionSwitchClass.dounlocking = true;
                        bool ret = UnLockByTimeOut();
                        if (!ret)
                        {
                            isunlockfail = true;
                        }
                    }
                }
                // 信号无效，启动或继续延迟计时器
                else
                {
                    HandleInvalidSignal(islocked);
                }
            }
        }

        // 新增：处理无效信号
        private void HandleInvalidSignal(bool islocked)
        {
            if (islocked) return; // 已锁定状态不处理

            if (!isWaitingForLock)
            {
                // 开始计时
                signalLossStartTime = DateTime.Now;
                isWaitingForLock = true;
                lockNotificationSent = false;
                Console.WriteLine("蓝牙信号弱或丢失，开始计时...");
            }
            else if (signalLossStartTime.HasValue)
            {
                // 计算已等待时间
                TimeSpan elapsed = DateTime.Now - signalLossStartTime.Value;
                int remainingSeconds = lockDelaySeconds - (int)elapsed.TotalSeconds;

                // 1秒后发送通知
                if (!lockNotificationSent && elapsed.TotalSeconds >= 1)
                {
                    ShowLockNotification(remainingSeconds > 0 ? remainingSeconds : 0);
                    lockNotificationSent = true;
                }

                // 达到延迟时间，执行锁定
                if (elapsed.TotalSeconds >= lockDelaySeconds)
                {
                    Console.WriteLine($"延迟 {lockDelaySeconds} 秒后执行锁定");
                    if (isautolock)
                    {
                        if (sessionSwitchClass.isUnlockBySoft == false && manualunlock == true)
                        {
                            Console.WriteLine("非软件解锁，不干预！");
                            ResetLockDelayTimer();
                            return;
                        }
                        Console.WriteLine("信号强度弱，锁屏！");
                        sessionSwitchClass.dolocking = true;
                        LockByTimeOut();
                    }
                    ResetLockDelayTimer();
                }
                else
                {
                    Console.WriteLine($"等待锁定... 剩余 {remainingSeconds} 秒");
                }
            }
        }

        // 新增：重置锁定延迟计时器
        private void ResetLockDelayTimer()
        {
            if (isWaitingForLock)
            {
                Console.WriteLine("信号恢复，取消锁定");
                signalLossStartTime = null;
                isWaitingForLock = false;
                lockNotificationSent = false;
            }
        }

        private void rdbclassic_CheckedChanged(object sender, EventArgs e)
        {
            setBluetoothType();
        }

        private void rdbble_CheckedChanged(object sender, EventArgs e)
        {
            setBluetoothType(); 
        }
        private void handlerPairingReq(DeviceInformationCustomPairing CP, DevicePairingRequestedEventArgs DPR)
        {
            DPR.Accept("0"); 
        }
        private void button2_Click(object sender, EventArgs e)
        {
            try { 
            if (lstbldevice.SelectedIndex < 0 || lstbldevice.SelectedIndex >= lstbldevice.Items.Count)
            {
                MessageBox.Show(this, "请选择有效的蓝牙设备!");
                return;
            }

            var selectedDevice = deviceAddresses[lstbldevice.Items[lstbldevice.SelectedIndex].ToString()];
            if(selectedDevice.Type=="BLE")
            {
                var address = selectedDevice.Address;
                BluetoothAddress ad = BluetoothAddress.Parse(address).ToUInt64();
                BluetoothLEDevice bluetoothLEDevice = BluetoothLEDevice.FromBluetoothAddressAsync(ad).GetAwaiter().GetResult();
                bluetoothLEDevice.DeviceInformation.Pairing.Custom.PairingRequested += handlerPairingReq;

                var prslt = bluetoothLEDevice.DeviceInformation.Pairing.UnpairAsync().GetAwaiter().GetResult();

                BluetoothLEDevice bleDevice = bluetoothLEDevice;
                var gatt = bleDevice.GetGattServicesAsync(BluetoothCacheMode.Cached).GetAwaiter().GetResult();
                var servv = gatt.Services[1];

                MessageBox.Show(this, prslt.Status.ToString());
            }
            else
            {
                MessageBox.Show(this,"经典蓝牙设备请手动操作！");
            }
            }catch(Exception ex)
            {
                MessageBox.Show(this, "取消配对发生错误："+ex.Message);
            }
        }
         
        private void checkBox1_Click(object sender, EventArgs e)
        {
            var ischeck = checkBox1.Checked;

            if(ischeck)
            {
                if(AutoStartHelper.IsExists()==false)
                {
                    var ret = AutoStartHelper.AddStart();
                    if(ret==false)
                    { 
                        MessageBox.Show(this, "添加自启失败！");
                    }
                }
            }
            else
            {
                var ret =  AutoStartHelper.RemoveStart();
                if (ret == false)
                {
                    MessageBox.Show(this, "移除自启失败！");
                }
            } 
            checkBox1.Checked = AutoStartHelper.IsExists();
        }
         
        private void ckb_autolock_Click(object sender, EventArgs e)
        {
             
            var ischeck = ckb_autolock.Checked;
            OperateIniFile.WriteIniInt("setting", "autolock", ischeck ? 1 : 0);
            reloadLockConfig();
        }
        /// <summary>
        /// 重载锁定配置
        /// </summary>
        private void reloadLockConfig()
        {
            try
            {
                isautolock = OperateIniFile.ReadIniInt("setting", "autolock", 1) == 1;
                isautounlock = OperateIniFile.ReadIniInt("setting", "autounlock", 1) == 1;
                manuallock = OperateIniFile.ReadIniInt("setting", "manuallock", 1) == 1;

                manualunlock = OperateIniFile.ReadIniInt("setting", "manualunlock", 0) == 1;
                ckb_autolock.Checked = isautolock;
                ckb_autounlock.Checked = isautounlock;
                ckb_manuallock.Checked = manuallock;
                ckb_manuclunlock.Checked = manualunlock;
            }

            catch (Exception ex)
            {
                Console.WriteLine(ex.Message);
            }
        }

        private bool isautolock = false;
        private bool isautounlock = false;
        /// <summary>
        /// true为不干预人工锁定
        /// </summary>
        private bool manuallock = true;
        /// <summary>
        /// true为不干预人工解锁
        /// </summary>
        private bool manualunlock = false;

        private void ckb_autounlock_Click(object sender, EventArgs e)
        {
            var ischeck = ckb_autounlock.Checked;
            OperateIniFile.WriteIniInt("setting", "autounlock", ischeck ? 1 : 0);
            reloadLockConfig();
        }

        private void ckb_manual_Click(object sender, EventArgs e)
        {
            var ischeck = ckb_manuallock.Checked;
            OperateIniFile.WriteIniInt("setting", "manuallock", ischeck ? 1 : 0);
            reloadLockConfig(); 
        }

        private void ckb_manuclunlock_Click(object sender, EventArgs e)
        {
            var ischeck = ckb_manuclunlock.Checked;
            OperateIniFile.WriteIniInt("setting", "manualunlock", ischeck ? 1 : 0);
            reloadLockConfig();
        }

        private void button3_Click(object sender, EventArgs e)
        {
            sessionSwitchClass.dolocking = true;
            sessionSwitchClass.isLockBySoft = true;
            WanClient.LockPc();  
        }

        private void 锁屏ToolStripMenuItem_Click(object sender, EventArgs e)
        { 
            sessionSwitchClass.dolocking = true;
            sessionSwitchClass.isLockBySoft = true;
            WanClient.LockPc();
        }

        private void 隐藏ToolStripMenuItem_Click(object sender, EventArgs e)
        {
            this.Close();
        }
    }
}
