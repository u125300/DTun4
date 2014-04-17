﻿Imports SharpPcap
Imports System.IO
Imports System.Net.Sockets
Imports System.Net
Imports System.Text
Imports PacketDotNet
Imports System.Security.Cryptography
Public Class Library
    Dim device As ICaptureDevice
    Public listener As UdpClient = New UdpClient()
    Public groupEP As IPEndPoint
    Dim source As New IPEndPoint(IPAddress.Any, 4955)
    Public IP As String
    Public log1 As StreamWriter '= New StreamWriter("log.txt", True)
#If DEBUG Then
    Dim remote As String = "192.168.1.2"
#Else
    Dim remote As String = "188.116.56.69"
#End If

    Public updateusers As Boolean = False
    Public users As String()
    Public oldusers() As String = {""}
    Public conn As Boolean = False
    Dim serverrsa As New RSACryptoServiceProvider()
    Dim aespass As String
    Public state As Integer = 0

    Public chatlines As New List(Of String)
    Dim chatsender As New UdpClient()

    Dim thr As Threading.Thread
    Sub Main(c As String())
        If Not log1 Is Nothing Then
            log1.Close()
        End If
        log1 = New StreamWriter("log.txt", True)
        log1.WriteLine("Preparing...")
        Dim cname As String = c(0)
        Dim nname As String = c(1)
        Dim staticip As Boolean = c(2)
#If Not Debug Then
        Dim w As New MyWebClient
        w.Headers.Add("user-agent", "Mozilla/5.0 (Windows NT 6.0) AppleWebKit/537.11 (KHTML, like Gecko) Chrome/23.0.1271.64 Safari/537.11")
        'remote = w.DownloadString("http://dtun4.disahome.tk/data/ip.txt")
        remote = Dns.GetHostEntry("apps.disahome.tk").AddressList(0).ToString
        Try
            serverrsa.FromXmlString(w.DownloadString("http://dtun4.disahome.tk/data/rsapubkey.txt"))
        Catch
            Try
                serverrsa.FromXmlString(w.DownloadString("http://dtun4.disahome.tk/data/rsapubkey.txt"))
            Catch
                Try
                    serverrsa.FromXmlString(w.DownloadString("http://dtun4.disahome.tk/data/rsapubkey.txt"))
                Catch
                    state = 7
                    MsgBox("Can't download server public RSA key.")
                    Exit Sub
                End Try
            End Try
        End Try
        w.Dispose()
#End If
        log1.WriteLine("Received IP and public key")
        state = 1
        Threading.Thread.Sleep(450)

        aespass = RandKey(20)

        log1.WriteLine("Generated AES key")
        state = 2
        Threading.Thread.Sleep(450)

        If staticip Then
            IP = getIP()
            If IP = "31.0.0.10" Then
                IP = "DONOTWANT"
            End If
        Else
            IP = "DONOTWANT"
        End If

        log1.WriteLine("Connecting to DTun4 Server")
        state = 3
        Threading.Thread.Sleep(700)

        groupEP = New IPEndPoint(IPAddress.Parse(remote), 4955)
        source = groupEP
        listener.Send(Encoding.Default.GetBytes(String.Format("HELO*{0}*{1}*{2}*{3}", cname, nname, IP, System.Text.Encoding.Default.GetString(serverrsa.Encrypt(System.Text.Encoding.Default.GetBytes(aespass), True)))), Encoding.Default.GetByteCount(String.Format("HELO*{0}*{1}*{2}*{3}", cname, nname, IP, System.Text.Encoding.Default.GetString(serverrsa.Encrypt(System.Text.Encoding.Default.GetBytes(aespass), True)))), groupEP)

        Dim response() As String = Encoding.Default.GetString(listener.Receive(source)).Split({"*"c}, 3)
        IP = response(1)
        users = response(2).Split("^")


        updateusers = True
        Shell("netsh interface ip set address name=DTun4 source=static addr=" & response(1) & " mask=255.0.0.0 gateway=none", AppWinStyle.Hide, True, -1)
        'Dim localIPs As IPAddress() = Dns.GetHostAddresses(Dns.GetHostName())
        'For k As Integer = 0 To localIPs.GetUpperBound(0)
        '    If localIPs(k).ToString.StartsWith(response(0)) Then
        '        IP = localIPs(k).ToString
        '    End If
        'Next

        thr = New Threading.Thread(AddressOf ReceivePacket)
        thr.IsBackground = True

        log1.Flush()
        Dim devices As CaptureDeviceList = CaptureDeviceList.Instance()
        Dim chdev As Integer = -1

        'Dim log As String = ""
        log1.WriteLine("Connected with server")
        log1.WriteLine("Scanning for network devices...")
        state = 4
        Threading.Thread.Sleep(600)

        Dim i As Integer = -1
        For Each dev As ICaptureDevice In devices
            Dim info() As String = dev.ToString.Split(vbLf)
            For j As Integer = 0 To info.GetUpperBound(0)
                If info(j).Contains("FriendlyName: ") Then
                    If info(j).Replace("FriendlyName: ", "").StartsWith("DTun4") Then
                        chdev = i + 1
                        Exit For
                    End If
                End If
            Next
            'log = String.Format("{0}" & vbNewLine, dev.ToString)
            i += 1
        Next
        If chdev = -1 Then
            log1.WriteLine("DTun adapter was not found. Try reinstalling it.")
            MsgBox("DTun adapter was not found. Try reinstalling it.")
            state = 7
            Exit Sub
        End If
        log1.WriteLine("Device found. Connecting...")
        state = 5
        Threading.Thread.Sleep(500)


        device = devices(chdev)
        AddHandler device.OnPacketArrival, New SharpPcap.PacketArrivalEventHandler(AddressOf HandlePacket)
        device.Open(DeviceMode.Normal, 30)
        device.StartCapture()
        thr.Start()
        log1.WriteLine("Connected with device.")
        log1.WriteLine("Working...")
        log1.WriteLine()
        state = 6
        conn = True
        log1.Flush()
        'Console.ReadLine()
    End Sub
    Public Shared Function getIP()
        Dim strHostName As String = System.Net.Dns.GetHostName()
        For i As Integer = 0 To System.Net.Dns.GetHostByName(strHostName).AddressList.Count - 1
            If System.Net.Dns.GetHostByName(strHostName).AddressList(0).ToString().StartsWith("31.") Then
                Return System.Net.Dns.GetHostByName(strHostName).AddressList(0).ToString()
            End If
        Next
    End Function
    Sub SDTun()
        Try
            log1.Flush()
            thr.Abort()
            device.StopCapture()
            'log1.Close()
        Catch
        End Try
    End Sub


    Sub HandlePacket(sender As Object, e As CaptureEventArgs)
        Dim packet As Byte() = e.Packet.Data
        log1.Write("Captured packet: ")
        Dim pack As Packet = PacketDotNet.Packet.ParsePacket(e.Packet.LinkLayerType, e.Packet.Data)
        Dim ip1 As IpPacket = IpPacket.GetEncapsulated(pack)
        Dim arp As ARPPacket = ARPPacket.GetEncapsulated(pack)

        If (Not ip1 Is Nothing) Then
            ParseTCPIP(pack, packet)
        ElseIf (Not arp Is Nothing) Then
            ParseARP(pack, packet)
        End If


        If (ip1 Is Nothing) And (arp Is Nothing) Then
            log1.WriteLine("nonIP nor arp packet")
            Exit Sub
        End If

        log1.Flush()
    End Sub

    Sub SendMessage(mes As String)
        chatsender.Send(System.Text.Encoding.Default.GetBytes("CHAT" & mes), System.Text.Encoding.Default.GetByteCount("CHAT" & mes), New IPEndPoint(IPAddress.Parse("31.255.255.255"), 4956))
        chatlines.Add("You: " & mes)
    End Sub

    Sub ReceivePacket()
        While True
            Try
                source = groupEP
                Dim packet As Byte() = listener.Receive(source)
                Dim message As String = Encoding.Default.GetString(packet)
                If (message.StartsWith("KFINE")) Then
                    users = Encoding.Default.GetString(packet).Substring(5).Split("^")
                    If Not DirectCast(oldusers, IStructuralEquatable).Equals(users, StructuralComparisons.StructuralEqualityComparer) Then
                        oldusers = users
                        updateusers = True
                    End If
                    Continue While
                End If
                If (Encoding.Default.GetString(packet).StartsWith("RECONNPLS")) Then
                    conn = False
                    thr.Abort()
                    device.StopCapture()
                    Exit Sub
                End If
                packet = AES_Decrypt(packet)

                

                If packet Is {0} Then
                    Continue While
                End If
                Dim pack As Packet = PacketDotNet.Packet.ParsePacket(LinkLayers.Ethernet, packet)
                Dim ip1 As IpPacket = IpPacket.GetEncapsulated(pack)
                Dim arp As ARPPacket = ARPPacket.GetEncapsulated(pack)

                

                If (Not ip1 Is Nothing) Then
                    'If ip1.DestinationAddress.Equals(IPAddress.Parse(IP)) Then

                    message = Encoding.Default.GetString(packet)
                    If message.Contains("CHAT") Then
                        chatlines.Add(ip1.SourceAddress.ToString & ":" & message.Substring(message.IndexOf("C")).Replace("CHAT", ""))
                        log1.WriteLine("Created chat message from {0}", ip1.SourceAddress.ToString)
                        Continue While
                    End If

                    device.SendPacket(packet)
                    log1.WriteLine("Created IP packet from {0}", ip1.SourceAddress.ToString)
                    'Else
                    ' log1.WriteLine("Received IP packed intended to another device. Skipped.")
                    'End If
                End If

                If (Not arp Is Nothing) Then
                    'If arp.TargetProtocolAddress.ToString = IP Then
                    device.SendPacket(packet)
                    log1.WriteLine("Created ARP packet from {0}", arp.SenderProtocolAddress.ToString)
                    'Else
                    'log1.WriteLine("Received ARP packed intended to another device. Skipped.")
                    'End If
                End If


                log1.Flush()
            Catch e As Exception
            End Try
        End While
    End Sub

    Sub ParseTCPIP(pack As Packet, Packet As Byte())
        Dim ip1 As IpPacket = IpPacket.GetEncapsulated(pack)
        log1.Write("IP packet: ")
        If ip1.Version = IpVersion.IPv4 Then
            'And ip1.SourceAddress.Equals(IPAddress.Parse(IP))
            'If Not ip1.DestinationAddress.Equals(IPAddress.Parse(IP)) And Not ip1.DestinationAddress.Equals(IPAddress.Parse("31.255.255.255")) Then
            If ip1.SourceAddress.Equals(IPAddress.Parse(IP)) Then
                Dim groupEP As New IPEndPoint(IPAddress.Parse(remote), 4955)
                Packet = AES_Encrypt(Packet)
                listener.Send(Packet, Packet.Count(), groupEP)
                log1.WriteLine("Sent to {0}", ip1.DestinationAddress.ToString)
            Else
                log1.WriteLine("Local packet from {0}. Skipped", ip1.SourceAddress.ToString)
            End If
        Else
            log1.WriteLine("v6Packet")
        End If

    End Sub

    Sub ParseARP(pack As Packet, Packet As Byte())
        Dim arp As ARPPacket = ARPPacket.GetEncapsulated(pack)
        log1.Write("ARP packet: ")
        'And arp.SenderProtocolAddress.ToString = IP
        'If Not arp.TargetProtocolAddress.ToString = IP Then
        If arp.SenderProtocolAddress.ToString = IP Then
            Dim groupEP As New IPEndPoint(IPAddress.Parse(remote), 4955)
            listener.Send(AES_Encrypt(Packet), AES_Encrypt(Packet).Count(), groupEP)
            log1.WriteLine("Sent to {0}", arp.TargetProtocolAddress.ToString)
        Else
            log1.WriteLine("Local packet. Skipped")
        End If
    End Sub

    

    Public Function AES_Decrypt(ByVal in1 As Byte(), Optional ByVal pass As String = "") As Byte()
        Dim input As String = Convert.ToBase64String(in1)

        If pass = "" Then
            pass = aespass
        End If
        Dim AES As New System.Security.Cryptography.RijndaelManaged
        Dim Hash_AES As New System.Security.Cryptography.MD5CryptoServiceProvider
        Dim decrypted As String = ""
        Try
            Dim hash(31) As Byte
            Dim temp As Byte() = Hash_AES.ComputeHash(System.Text.ASCIIEncoding.ASCII.GetBytes(pass))
            Array.Copy(temp, 0, hash, 0, 16)
            Array.Copy(temp, 0, hash, 15, 16)
            AES.Key = hash
            AES.Mode = CipherMode.ECB
            Dim DESDecrypter As System.Security.Cryptography.ICryptoTransform = AES.CreateDecryptor
            Dim Buffer As Byte() = Convert.FromBase64String(Input)
            decrypted = System.Text.ASCIIEncoding.ASCII.GetString(DESDecrypter.TransformFinalBlock(Buffer, 0, Buffer.Length))
            Return Convert.FromBase64String(decrypted)
        Catch ex As Exception
            Return {0}
        End Try
    End Function
    Public Function AES_Encrypt(ByVal in1 As Byte(), Optional ByVal pass As String = "") As Byte()
        Dim input As String = Convert.ToBase64String(in1)

        If pass = "" Then
            pass = aespass
        End If
        Dim AES As New System.Security.Cryptography.RijndaelManaged
        Dim Hash_AES As New System.Security.Cryptography.MD5CryptoServiceProvider
        Dim encrypted As String = ""
        Try
            Dim hash(31) As Byte
            Dim temp As Byte() = Hash_AES.ComputeHash(System.Text.ASCIIEncoding.ASCII.GetBytes(pass))
            Array.Copy(temp, 0, hash, 0, 16)
            Array.Copy(temp, 0, hash, 15, 16)
            AES.Key = hash
            AES.Mode = CipherMode.ECB
            Dim DESEncrypter As System.Security.Cryptography.ICryptoTransform = AES.CreateEncryptor
            Dim Buffer As Byte() = System.Text.ASCIIEncoding.ASCII.GetBytes(Input)
            encrypted = Convert.ToBase64String(DESEncrypter.TransformFinalBlock(Buffer, 0, Buffer.Length))
            Return Convert.FromBase64String(encrypted)
        Catch ex As Exception
            Return {0}
        End Try
    End Function


    Public Function Rand(ByVal Min As Integer, ByVal Max As Integer) As Integer
        Static Generator As System.Random = New System.Random()
        Return Generator.Next(Min, Max)
    End Function

    Function RandKey(RequiredStringLength As Integer) As String
        Dim CharArray() As Char = "12345ABCDEFGHIJKLMNOPQRSTUVWXYZ67890abcdefghijklmnopqrstuvwxyz".ToCharArray
        Dim sb As New System.Text.StringBuilder

        For index As Integer = 1 To RequiredStringLength
            sb.Append(CharArray(Rand(0, CharArray.Length)))
        Next

        Return sb.ToString

    End Function
End Class

Class MyWebClient
    Inherits WebClient

    Protected Overrides Function GetWebRequest(uri As Uri) As WebRequest

        Dim w As WebRequest = MyBase.GetWebRequest(uri)
        w.Timeout = 6000
        Return w
    End Function
End Class