using System.Windows.Controls;
using Sortify.Models;

namespace Sortify.Views;

/// <summary>A year's shareable summary, laid out at 1080 by 1350 pixels.</summary>
public partial class YearReviewCard : UserControl
{
    public YearReviewCard(YearReview review)
    {
        InitializeComponent();
        DataContext = review;
    }
}
