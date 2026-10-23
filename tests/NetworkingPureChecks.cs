using System;
using System.Xml;
using NetworkRecovery;
class NetworkingPureChecks {
 static int passed;
 static void Assert(bool value, string label) { if (!value) throw new Exception(label); passed++; }
 static void Reject(WifiNetwork n, string password) { try { NetworkManager.BuildNewProfileXml(n,password,"test"); } catch(ArgumentException) { passed++; return; } throw new Exception("Expected validation failure"); }
 static void Main() {
  WifiNetwork n=new WifiNetwork { SsidHex="E4B8ADE69687263C",IsSupportedPersonal=true,Auth=7,Cipher=4 };
  string password="ab<&\"'cdef";
  string xml=NetworkManager.BuildNewProfileXml(n,password,"profile<&\"'");
  XmlDocument doc=new XmlDocument(); doc.LoadXml(xml);
  XmlNamespaceManager ns=new XmlNamespaceManager(doc.NameTable); ns.AddNamespace("w","http://www.microsoft.com/networking/WLAN/profile/v1");
  Assert(doc.SelectSingleNode("//w:keyMaterial",ns).InnerText==password,"password escaping roundtrip");
  Assert(doc.SelectSingleNode("//w:hex",ns).InnerText==n.SsidHex,"SSID exact bytes");
  Assert(doc.SelectSingleNode("//w:connectionMode",ns).InnerText=="auto","auto connection");
  Assert(doc.SelectSingleNode("//w:authentication",ns).InnerText=="WPA2PSK","correct WPA2 type");
  Assert(doc.SelectSingleNode("/w:WLANProfile/w:name",ns).InnerText=="profile<&\"'","profile name escaping");
  Reject(n,"short"); Reject(n,new string('x',64));
  Assert(NetworkManager.BuildNewProfileXml(n,new string('A',64),"key").Contains("networkKey"),"WPA2 hex key");
  n.Auth=9; Assert(NetworkManager.BuildNewProfileXml(n,"x","sae").Contains("WPA3SAE"),"WPA3 type"); Reject(n,"");
  n.IsOpen=true;n.IsSupportedPersonal=false;Assert(!NetworkManager.BuildNewProfileXml(n,null,"open").Contains("keyMaterial"),"open has no password");
  n.SsidHex="GG";Reject(n,null);
  Console.WriteLine("Passed "+passed+" isolated XML validation checks. No network APIs invoked.");
 }
}