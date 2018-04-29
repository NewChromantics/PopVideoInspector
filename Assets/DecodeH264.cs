using System.Collections;
using System.Collections.Generic;
using UnityEngine;


[RequireComponent(typeof(JustH264))]
public class DecodeH264 : MonoBehaviour {

	public JustH264 Decoder	{ get { return GetComponent<JustH264>(); }}

	public void Decode(byte[] Header,byte[] Packet)
	{
		Decoder.writeH264(Header);
		Decoder.writeH264(Packet);
	}
}
