namespace TLab.VKeyborad
{
    public interface IInputHolder
    {
        void OnValueChanged(string value);

        bool GetInitValue(out string value);
    }
}
