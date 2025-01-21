using System.ComponentModel;
using System.Runtime.CompilerServices;

namespace TwitchGpt.Entities;

public class ApiCredentials : INotifyPropertyChanged
{
    private string _apiUserName;
    
    private string _apiUserId;
    
    private string _clientId;
    
    private string _secret;
    
    private string _accessToken;
    
    public int BotId { get; set; }

    public string ApiUserName
    {
        get => _apiUserName;
        set
        {
            if (_apiUserName == value)
                return;
            
            _apiUserName = value;
            NotifyPropertyChanged();
        }
    }

    public string ApiUserId
    {
        get => _apiUserId;
        set
        {
            if (_apiUserId == value)
                return;
            
            _apiUserId = value;
            NotifyPropertyChanged();
        }
    }

    public string ClientId
    {
        get => _clientId;
        set
        {
            if (_clientId == value)
                return;
            
            _clientId = value;
            NotifyPropertyChanged();
        }
    }

    public string Secret
    {
        get => _secret;
        set
        {
            if (_secret == value)
                return;
            
            _secret = value;
            NotifyPropertyChanged();
        }
    }

    public string AccessToken
    {
        get => _accessToken;
        set
        {
            if (_accessToken == value)
                return;
            
            _accessToken = value;
            NotifyPropertyChanged();
        }
    }

    public event PropertyChangedEventHandler PropertyChanged;
    
    protected void NotifyPropertyChanged([CallerMemberName] string propertyName = "")
    {
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
    }
}
