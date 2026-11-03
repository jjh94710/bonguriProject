using TMPro;
using UnityEngine;
using UnityEngine.UI;

[DisallowMultipleComponent]
public sealed class CellOperatorUI : MonoBehaviour
{
    [SerializeField] CellController controller;
    [SerializeField] Button startButton, stopButton, decreaseButton, increaseButton, basicButton, policyButton;
    [SerializeField] TMP_Text statusText, intervalText;
    [SerializeField, Range(1,30)] float previewInterval=10;
    float nextRefresh;
    void OnEnable(){
        startButton?.onClick.AddListener(StartCell);stopButton?.onClick.AddListener(StopCell);
        decreaseButton?.onClick.AddListener(Decrease);increaseButton?.onClick.AddListener(Increase);
        basicButton?.onClick.AddListener(Basic);policyButton?.onClick.AddListener(Policy);Refresh();
    }
    void OnDisable(){
        startButton?.onClick.RemoveListener(StartCell);stopButton?.onClick.RemoveListener(StopCell);
        decreaseButton?.onClick.RemoveListener(Decrease);increaseButton?.onClick.RemoveListener(Increase);
        basicButton?.onClick.RemoveListener(Basic);policyButton?.onClick.RemoveListener(Policy);
    }
    void Update(){if(!Application.isPlaying||Time.unscaledTime<nextRefresh)return;nextRefresh=Time.unscaledTime+.2f;Refresh();}
    void StartCell(){if(Application.isPlaying&&controller!=null)controller.Run(0);Refresh();}
    void StopCell(){if(Application.isPlaying&&controller!=null)controller.RequestCycleStop();Refresh();}
    void Decrease(){Adjust(-1);} void Increase(){Adjust(1);}
    void Adjust(float delta){if(!Application.isPlaying||controller==null||controller.Settings==null)return;controller.Settings.feedInterval=Mathf.Clamp(controller.Settings.feedInterval+delta,1,30);Refresh();}
    void Basic(){SetMode(ControlMode.Basic);} void Policy(){SetMode(ControlMode.Policy);}
    void SetMode(ControlMode mode){if(Application.isPlaying&&controller!=null)controller.SetMode(mode);Refresh();}
    public void Refresh(){
        bool connected=controller!=null;bool ready=Application.isPlaying&&connected&&controller.Settings!=null;
        bool running=ready&&controller.IsRunning;
        if(statusText!=null){statusText.text=!connected?"공정 미연결":!Application.isPlaying?"운전 준비":running?"가동 중":"정지";statusText.color=running?new Color(.45f,.9f,.7f):new Color(.75f,.8f,.86f);}
        float interval=ready?controller.Settings.feedInterval:previewInterval;
        if(intervalText!=null)intervalText.text=interval.ToString("0.#")+"초";
        if(startButton!=null)startButton.interactable=ready&&!running;
        if(stopButton!=null)stopButton.interactable=running;
        if(decreaseButton!=null)decreaseButton.interactable=ready&&interval>1;
        if(increaseButton!=null)increaseButton.interactable=ready&&interval<30;
        if(basicButton!=null)basicButton.interactable=ready;
        if(policyButton!=null)policyButton.interactable=ready&&controller.HasPolicy;
        ControlMode selected=ready?controller.PendingMode:ControlMode.Basic;
        Tint(basicButton,selected==ControlMode.Basic);Tint(policyButton,selected==ControlMode.Policy);
    }
    static void Tint(Button button,bool selected){if(button==null)return;var colors=button.colors;colors.normalColor=selected?new Color(.32f,.46f,.58f,1):new Color(.18f,.23f,.29f,1);colors.disabledColor=selected?new Color(.29f,.36f,.42f,.8f):new Color(.16f,.2f,.25f,.7f);button.colors=colors;}
}
