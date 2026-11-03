using System;
using UnityEngine;
using UnityEngine.UI;

[DisallowMultipleComponent]
public sealed class SideCameraNavigation : MonoBehaviour
{
    [SerializeField] Camera[] views = new Camera[3];
    [SerializeField] Camera[] otherViewCameras = Array.Empty<Camera>();
    [SerializeField] Button leftButton;
    public int CurrentViewIndex { get; private set; }
    public string CurrentViewName => views != null && CurrentViewIndex < views.Length && views[CurrentViewIndex] != null ? views[CurrentViewIndex].name : "";
    bool[] initialCameraStates, initialListenerStates;
    Camera[] managed;
    void OnEnable()
    {
        if (!Application.isPlaying) return;
        if (views == null || views.Length != 3 || Array.Exists(views, c => c == null) || leftButton == null)
        { Debug.LogError("[SideCameraNavigation] 카메라 3개와 왼쪽 버튼을 연결하세요.", this); enabled = false; return; }
        managed = new Camera[views.Length + otherViewCameras.Length];
        Array.Copy(views, managed, views.Length); Array.Copy(otherViewCameras, 0, managed, views.Length, otherViewCameras.Length);
        initialCameraStates = new bool[managed.Length]; initialListenerStates = new bool[managed.Length];
        for (int i = 0; i < managed.Length; i++)
        {
            if (managed[i] == null) continue;
            initialCameraStates[i] = managed[i].enabled;
            var listener = managed[i].GetComponent<AudioListener>();
            initialListenerStates[i] = listener != null && listener.enabled;
        }
        leftButton.onClick.AddListener(NextView);
        SelectView(0);
    }
    public void NextView()
    {
        if (!Application.isPlaying || views == null || CurrentViewIndex >= views.Length - 1) return;
        SelectView(CurrentViewIndex + 1);
    }
    public void SelectView(int index)
    {
        if (!Application.isPlaying || managed == null || index < 0 || index >= views.Length) return;
        CurrentViewIndex = index;
        for (int i = 0; i < managed.Length; i++)
        {
            var camera = managed[i]; if (camera == null) continue;
            bool selected = camera == views[index]; camera.enabled = selected;
            var listener = camera.GetComponent<AudioListener>(); if (listener != null) listener.enabled = selected;
        }
        leftButton.interactable = index < views.Length - 1;
    }
    void OnDisable()
    {
        if (leftButton != null) leftButton.onClick.RemoveListener(NextView);
        if (managed == null) return;
        for (int i = 0; i < managed.Length; i++)
        {
            if (managed[i] == null) continue;
            managed[i].enabled = initialCameraStates[i];
            var listener = managed[i].GetComponent<AudioListener>(); if (listener != null) listener.enabled = initialListenerStates[i];
        }
        managed = null;
    }
}
