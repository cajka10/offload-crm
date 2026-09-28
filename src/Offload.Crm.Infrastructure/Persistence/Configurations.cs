using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Offload.Crm.Domain.Entities;

namespace Offload.Crm.Infrastructure.Persistence;

// Max lengths mirror the validation rules in D-12. FK delete behaviors follow D-07.
internal sealed class ApplicationUserConfiguration : IEntityTypeConfiguration<ApplicationUser>
{
    public void Configure(EntityTypeBuilder<ApplicationUser> b)
    {
        b.Property(x => x.FullName).HasMaxLength(200).IsRequired();
    }
}

internal sealed class CustomerConfiguration : IEntityTypeConfiguration<Customer>
{
    public void Configure(EntityTypeBuilder<Customer> b)
    {
        b.Property(x => x.CompanyName).HasMaxLength(200).IsRequired();
        b.Property(x => x.RegistrationNo).HasMaxLength(20);
        b.Property(x => x.RegistrationNoNormalized).HasMaxLength(20);
        b.Property(x => x.VatId).HasMaxLength(20);
        b.Property(x => x.Email).HasMaxLength(254);
        b.Property(x => x.Phone).HasMaxLength(30);
        b.Property(x => x.Website).HasMaxLength(500);
        b.Property(x => x.Street).HasMaxLength(200);
        b.Property(x => x.City).HasMaxLength(200);
        b.Property(x => x.PostalCode).HasMaxLength(200);
        b.Property(x => x.Country).HasMaxLength(200);
        b.Property(x => x.Notes).HasMaxLength(4000);

        b.HasIndex(x => x.CompanyName);
        b.HasIndex(x => x.RegistrationNoNormalized)
            .IsUnique()
            .HasFilter("\"RegistrationNoNormalized\" IS NOT NULL");
    }
}

internal sealed class ContactConfiguration : IEntityTypeConfiguration<Contact>
{
    public void Configure(EntityTypeBuilder<Contact> b)
    {
        b.Property(x => x.FirstName).HasMaxLength(100);
        b.Property(x => x.LastName).HasMaxLength(100).IsRequired();
        b.Property(x => x.Position).HasMaxLength(100);
        b.Property(x => x.Email).HasMaxLength(254);
        b.Property(x => x.Phone).HasMaxLength(30);
        b.Property(x => x.Notes).HasMaxLength(4000);

        b.HasOne(x => x.Customer)
            .WithMany(x => x.Contacts)
            .HasForeignKey(x => x.CustomerId)
            .OnDelete(DeleteBehavior.Cascade);

        b.HasIndex(x => x.LastName);
    }
}

internal sealed class ProjectStatusConfiguration : IEntityTypeConfiguration<ProjectStatus>
{
    public void Configure(EntityTypeBuilder<ProjectStatus> b)
    {
        b.Property(x => x.Name).HasMaxLength(50).IsRequired();
        b.Property(x => x.Color).HasMaxLength(9).IsRequired();

        b.HasIndex(x => x.Name).IsUnique();

        // At most one default status (D-02).
        b.HasIndex(x => x.IsDefault).IsUnique().HasFilter("\"IsDefault\" = TRUE");
    }
}

internal sealed class ProjectConfiguration : IEntityTypeConfiguration<Project>
{
    public void Configure(EntityTypeBuilder<Project> b)
    {
        b.Property(x => x.Name).HasMaxLength(200).IsRequired();
        b.Property(x => x.Description).HasMaxLength(4000);
        b.Property(x => x.Notes).HasMaxLength(4000);
        b.Property(x => x.EstimatedValue).HasPrecision(18, 2);
        b.Property(x => x.ActualValue).HasPrecision(18, 2);

        b.HasOne(x => x.Customer)
            .WithMany(x => x.Projects)
            .HasForeignKey(x => x.CustomerId)
            .OnDelete(DeleteBehavior.Restrict);

        b.HasOne(x => x.Status)
            .WithMany(x => x.Projects)
            .HasForeignKey(x => x.StatusId)
            .OnDelete(DeleteBehavior.Restrict);

        b.HasOne(x => x.ResponsibleUser)
            .WithMany()
            .HasForeignKey(x => x.ResponsibleUserId)
            .OnDelete(DeleteBehavior.Restrict);

        b.HasIndex(x => x.Name);
        b.HasIndex(x => x.TargetDate);
    }
}

internal sealed class TaskItemConfiguration : IEntityTypeConfiguration<TaskItem>
{
    public void Configure(EntityTypeBuilder<TaskItem> b)
    {
        b.ToTable("Tasks");

        b.Property(x => x.Title).HasMaxLength(200).IsRequired();
        b.Property(x => x.Description).HasMaxLength(4000);

        b.HasOne(x => x.Project)
            .WithMany(x => x.Tasks)
            .HasForeignKey(x => x.ProjectId)
            .OnDelete(DeleteBehavior.Cascade);

        b.HasOne(x => x.AssignedUser)
            .WithMany()
            .HasForeignKey(x => x.AssignedUserId)
            .OnDelete(DeleteBehavior.Restrict);

        b.HasIndex(x => x.Status);
        b.HasIndex(x => x.DueDate);
    }
}

internal sealed class ActivityConfiguration : IEntityTypeConfiguration<Activity>
{
    public void Configure(EntityTypeBuilder<Activity> b)
    {
        b.Property(x => x.Subject).HasMaxLength(200);
        b.Property(x => x.Description).HasMaxLength(4000).IsRequired();

        // Customer delete is only allowed without projects, so this removes customer-only activities (D-07).
        b.HasOne(x => x.Customer)
            .WithMany(x => x.Activities)
            .HasForeignKey(x => x.CustomerId)
            .OnDelete(DeleteBehavior.Cascade);

        // Project delete keeps its activities; they stay linked to the customer (D-07).
        b.HasOne(x => x.Project)
            .WithMany(x => x.Activities)
            .HasForeignKey(x => x.ProjectId)
            .OnDelete(DeleteBehavior.SetNull);

        b.HasOne(x => x.Author)
            .WithMany()
            .HasForeignKey(x => x.AuthorId)
            .IsRequired()
            .OnDelete(DeleteBehavior.Restrict);

        b.HasIndex(x => x.Date);

        b.ToTable(t => t.HasCheckConstraint(
            "CK_Activities_CustomerOrProject",
            "\"CustomerId\" IS NOT NULL OR \"ProjectId\" IS NOT NULL"));
    }
}
