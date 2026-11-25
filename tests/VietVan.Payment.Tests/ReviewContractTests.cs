using System.ComponentModel.DataAnnotations;
using Microsoft.AspNetCore.Authorization;
using Microsoft.EntityFrameworkCore;
using VietVan.API.Controllers;
using VietVan.API.Data;
using VietVan.API.Entities;
using Xunit;

public class ReviewContractTests
{
    private static bool Valid(ReviewRequest request) => Validator.TryValidateObject(request, new ValidationContext(request), new List<ValidationResult>(), true);

    [Theory]
    [InlineData(0, false)] [InlineData(1, true)] [InlineData(5, true)] [InlineData(6, false)]
    public void RatingMustBeOneToFive(int rating, bool expected) => Assert.Equal(expected, Valid(new ReviewRequest { Rating = rating, Comment = "Hay" }));

    [Fact]
    public void CommentLengthIsBounded() => Assert.False(Valid(new ReviewRequest { Rating = 5, Comment = new string('x', 2001) }));

    [Fact]
    public void WritesAndModerationRequireAuthorization()
    {
#pragma warning disable CS0618 // Các adapter cũ vẫn phải giữ xác thực trong giai đoạn chuyển đổi.
        foreach (var method in new[] { nameof(ReviewsController.Create), nameof(ReviewsController.Update), nameof(ReviewsController.Delete) })
            Assert.NotEmpty(typeof(ReviewsController).GetMethod(method)!.GetCustomAttributes(typeof(AuthorizeAttribute), true));
        foreach (var method in new[] { nameof(ReviewsController.AdminList), nameof(ReviewsController.Moderate) })
            Assert.Equal("Admin", Assert.Single(typeof(ReviewsController).GetMethod(method)!.GetCustomAttributes(typeof(AuthorizeAttribute), true).Cast<AuthorizeAttribute>()).Roles);
#pragma warning restore CS0618
    }

    [Fact]
    public void ModelEnforcesUniqueTargetsAndRestrictsDeletes()
    {
        var options = new DbContextOptionsBuilder<VietVanDbContext>().UseSqlServer("Server=localhost;Database=test;Integrated Security=true;TrustServerCertificate=true").Options;
        using var db = new VietVanDbContext(options);
        var entity = db.Model.FindEntityType(typeof(Review))!;
        Assert.Equal(2, entity.GetIndexes().Count(i => i.IsUnique && i.GetFilter() != null));
        Assert.All(entity.GetForeignKeys(), fk => Assert.Equal(DeleteBehavior.NoAction, fk.DeleteBehavior));
    }
}
