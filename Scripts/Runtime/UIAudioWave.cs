using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using UnityEngine;
using UnityEngine.UI;

namespace AudioBox.AudioWave
{
	public class UIAudioWave : Graphic, IDisposable
	{
		public AudioClip AudioClip
		{
			get => m_AudioClip;
			set
			{
				if (m_AudioClip == value)
					return;
				
				m_AudioClip = value;
				
				Render();
			}
		}

		public int Time
		{
			get => m_Time;
			set
			{
				if (m_Time == value)
					return;
				
				m_Time = value;
				
				ProcessTime();
			}
		}

		public float MinTime
		{
			get => m_MinTime;
			set
			{
				if (Mathf.Approximately(m_MinTime, value))
					return;
				
				m_MinTime = value;
				
				ProcessTime();
			}
		}

		public float MaxTime
		{
			get => m_MaxTime;
			set
			{
				if (Mathf.Approximately(m_MaxTime, value))
					return;
				
				m_MaxTime = value;
				
				ProcessTime();
			}
		}

		public Color MaxColor
		{
			get => m_MaxColor;
			set
			{
				if (m_MaxColor == value)
					return;
				
				m_MaxColor = value;
				
				ProcessColor();
			}
		}

		public override Material defaultMaterial
		{
			get
			{
				if (m_DefaultMaterial == null)
				{
					m_DefaultMaterial           = new Material(Shader.Find("UI/AudioWave"));
					m_DefaultMaterial.hideFlags = HideFlags.HideAndDontSave;
				}
				return m_DefaultMaterial;
			}
		}

		static Material m_DefaultMaterial;

		static readonly int m_BackgroundColorPropertyID  = Shader.PropertyToID("_BackgroundColor");
		static readonly int m_MaxColorPropertyID         = Shader.PropertyToID("_MaxColor");
		static readonly int m_MaxSamplesPropertyID       = Shader.PropertyToID("_MaxSamples");
		static readonly int m_MaxSamplesLengthPropertyID = Shader.PropertyToID("_MaxSamplesLength");
		static readonly int m_AvgColorPropertyID         = Shader.PropertyToID("_AvgColor");
		static readonly int m_AvgSamplesPropertyID       = Shader.PropertyToID("_AvgSamples");
		static readonly int m_AvgSamplesLengthPropertyID = Shader.PropertyToID("_AvgSamplesLength");
		static readonly int m_MinTimePropertyID          = Shader.PropertyToID("_MinTime");
		static readonly int m_MaxTimePropertyID          = Shader.PropertyToID("_MaxTime");

		[SerializeField] Color     m_BackgroundColor = new Color(0.12f, 0.12f, 0.12f, 1);
		[SerializeField] Color     m_MaxColor        = new Color(1, 0.5f, 0, 1);
		[SerializeField] Color     m_AvgColor        = new Color(1, 0.75f, 0, 1);
		[SerializeField] int       m_Time;
		[SerializeField] float     m_MinTime = -0.25f;
		[SerializeField] float     m_MaxTime = 0.75f;
		[SerializeField] AudioClip m_AudioClip;

		int                     m_Samples;
		int                     m_Frequency;
		double                  m_SamplesPerUnit;
		ComputeBuffer           m_EmptyBuffer;
		ComputeBuffer           m_MaxSamples;
		ComputeBuffer           m_AvgSamples;
		CancellationTokenSource m_TokenSource;

		readonly UIVertex[] m_Vertices =
		{
			new UIVertex(),
			new UIVertex(),
			new UIVertex(),
			new UIVertex(),
		};

		protected override void Awake()
		{
			base.Awake();
			
			ProcessTime();
			ProcessColor();
			Render();
		}

		protected override void OnEnable()
		{
			base.OnEnable();
			
			ProcessTime();
			ProcessColor();
			Render();
		}

		protected override void OnDestroy()
		{
			base.OnDestroy();
			
			if (this is IDisposable disposable)
				disposable.Dispose();
		}

		protected override void OnValidate()
		{
			base.OnValidate();
			
			ProcessTime();
			ProcessColor();
		}

		void ProcessTime()
		{
			int minTime = (int)((m_Time + m_MinTime * m_Frequency) / m_SamplesPerUnit);
			int maxTime = (int)((m_Time + m_MaxTime * m_Frequency) / m_SamplesPerUnit);
			
			material.SetInt(m_MinTimePropertyID, minTime);
			material.SetInt(m_MaxTimePropertyID, maxTime);
		}

		void ProcessColor()
		{
			material.SetColor(m_BackgroundColorPropertyID, m_BackgroundColor);
			material.SetColor(m_MaxColorPropertyID, m_MaxColor);
			material.SetColor(m_AvgColorPropertyID, m_AvgColor);
		}

		public void Render()
		{
			Rect rect = GetPixelAdjustedRect();
			
			if (m_AudioClip == null)
			{
				m_Samples        = 1;
				m_Frequency      = 1;
				m_SamplesPerUnit = 1;
			}
			else
			{
				m_Frequency      = m_AudioClip.frequency;
				m_Samples        = m_AudioClip.samples;
				m_SamplesPerUnit = m_Frequency / (rect.height / (m_MaxTime - m_MinTime)) * 4;
			}
			
			m_EmptyBuffer?.Release();
			m_EmptyBuffer?.Dispose();
			
			m_EmptyBuffer = new ComputeBuffer(4, 4);
			
			material.SetBuffer(m_MaxSamplesPropertyID, m_EmptyBuffer);
			material.SetBuffer(m_AvgSamplesPropertyID, m_EmptyBuffer);
			
			material.SetInt(m_MaxSamplesLengthPropertyID, m_EmptyBuffer.count);
			material.SetInt(m_AvgSamplesLengthPropertyID, m_EmptyBuffer.count);
			
			ProcessTime();
			ProcessColor();
			LoadAudioData(m_AudioClip);
		}

		async void LoadAudioData(AudioClip _AudioClip)
		{
			m_TokenSource?.Cancel();
			m_TokenSource?.Dispose();
			
			m_TokenSource = new CancellationTokenSource();
			
			CancellationToken token = m_TokenSource.Token;
			
			Task<float[]> maxDataTask = GetAudioData(_AudioClip, _Buffer => _Buffer.Max(Mathf.Abs), token);
			Task<float[]> avgDataTask = GetAudioData(_AudioClip, _Buffer => _Buffer.Average(Mathf.Abs), token);
			
			await Task.WhenAll(maxDataTask, avgDataTask);
			
			float[] maxData = maxDataTask.Result;
			float[] avgData = avgDataTask.Result;
			
			if (token.IsCancellationRequested)
				return;
			
			m_MaxSamples?.Dispose();
			m_AvgSamples?.Dispose();
			
			await Task.Yield();
			
			if (maxData.Length > 0)
			{
				m_MaxSamples = new ComputeBuffer(maxData.Length, 4);
				m_MaxSamples.SetData(maxData);
				material.SetBuffer(m_MaxSamplesPropertyID, m_MaxSamples);
				material.SetInt(m_MaxSamplesLengthPropertyID, m_MaxSamples.count);
			}
			
			if (avgData.Length > 0)
			{
				m_AvgSamples = new ComputeBuffer(avgData.Length, 4);
				m_AvgSamples.SetData(avgData);
				material.SetBuffer(m_AvgSamplesPropertyID, m_AvgSamples);
				material.SetInt(m_AvgSamplesLengthPropertyID, m_AvgSamples.count);
			}
			
			m_TokenSource?.Dispose();
			m_TokenSource = null;
		}

		async Task<float[]> GetAudioData(AudioClip _AudioClip, Func<float[], float> _Function, CancellationToken _Token = default)
		{
			if (_AudioClip == null)
				return Array.Empty<float>();
			
			int samples   = (int)(m_Samples / m_SamplesPerUnit);
			int threshold = samples / 30;
			
			float[] buffer = new float[(int)m_SamplesPerUnit];
			float[] data   = new float[samples];
			
			for (int i = 0; i < data.Length; i++)
			{
				if (_Token.IsCancellationRequested)
					break;
				
				_AudioClip.GetData(buffer, (int)(i * m_SamplesPerUnit));
				
				if (i % threshold == 0)
					await Task.Yield();
				
				data[i] = _Function(buffer);
			}
			
			return data;
		}

		protected override void OnPopulateMesh(VertexHelper _VertexHelper)
		{
			_VertexHelper.Clear();
			
			if (m_EmptyBuffer == null && m_MaxSamples == null && m_AvgSamples == null)
				return;
			
			Rect rect = GetPixelAdjustedRect();
			Color32 color32 = color;
			
			m_Vertices[0].position = new Vector3(rect.xMin, rect.yMin);
			m_Vertices[1].position = new Vector3(rect.xMin, rect.yMax);
			m_Vertices[2].position = new Vector3(rect.xMax, rect.yMax);
			m_Vertices[3].position = new Vector3(rect.xMax, rect.yMin);
			
			m_Vertices[0].uv0 = new Vector2(0, 0);
			m_Vertices[1].uv0 = new Vector2(0, 1);
			m_Vertices[2].uv0 = new Vector2(1, 1);
			m_Vertices[3].uv0 = new Vector2(1, 0);
			
			m_Vertices[0].color = color32;
			m_Vertices[1].color = color32;
			m_Vertices[2].color = color32;
			m_Vertices[3].color = color32;
			
			_VertexHelper.AddUIVertexQuad(m_Vertices);
		}

		void IDisposable.Dispose()
		{
			m_TokenSource?.Cancel();
			m_TokenSource?.Dispose();
			m_TokenSource = null;
			
			m_EmptyBuffer?.Release();
			m_EmptyBuffer?.Dispose();
			m_EmptyBuffer = null;
			
			m_MaxSamples?.Release();
			m_MaxSamples?.Dispose();
			m_MaxSamples = null;
			
			m_AvgSamples?.Release();
			m_AvgSamples?.Dispose();
			m_AvgSamples = null;
		}
	}
}